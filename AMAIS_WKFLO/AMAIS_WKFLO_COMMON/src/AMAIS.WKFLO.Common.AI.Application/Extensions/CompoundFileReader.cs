using System.Buffers.Binary;
using System.Text;

namespace AMAIS.WKFLO.Common.AI.Application.Extensions
{
    /// <summary>
    /// Minimal read-only reader for Compound File Binary (MS-CFB / OLE2) containers, enough to
    /// fetch a named root-level stream. It parses untrusted uploads, so every sector index is
    /// validated against the file length, chains are bounded by the sector count (cycle
    /// detection), no allocation is sized from an unchecked value and any inconsistency throws
    /// <see cref="InvalidDataException"/>.
    /// </summary>
    internal sealed class CompoundFileReader
    {
        private const int HeaderSize = 512;
        private const int DirectoryEntrySize = 128;
        private const int MiniSectorSize = 64;
        private const int HeaderDifatCount = 109;

        private const uint MaxRegularSector = 0xFFFFFFFA;
        private const uint EndOfChain = 0xFFFFFFFE;
        private const uint FreeSector = 0xFFFFFFFF;
        private const uint NoStream = 0xFFFFFFFF;

        private static readonly byte[] Signature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

        private readonly byte[] _file;
        private byte[]? _directory;
        private readonly int _sectorSize;
        private readonly long _sectorCount;
        private readonly uint _miniCutoff;
        private readonly uint[] _fat;
        private readonly uint _firstDirSector;
        private readonly uint _firstMiniFatSector;
        private readonly uint _miniFatSectorCount;

        private CompoundFileReader(byte[] file)
        {
            _file = file;

            if (file.Length < HeaderSize || !file.AsSpan(0, Signature.Length).SequenceEqual(Signature))
                throw new InvalidDataException("The file is not a compound (OLE2) document.");

            var sectorShift = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x1E));
            var miniShift = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(0x20));
            if (sectorShift is not (9 or 12) || miniShift != 6)
                throw new InvalidDataException("Unsupported compound file sector size.");

            _sectorSize = 1 << sectorShift;
            if (file.Length < _sectorSize)
                throw new InvalidDataException("The compound file is truncated.");

            // Sector n lives at (n + 1) * sectorSize; only whole sectors count.
            _sectorCount = file.Length / _sectorSize - 1;
            if (_sectorCount <= 0)
                throw new InvalidDataException("The compound file contains no sectors.");

            var fatSectorCount = U32(0x2C);
            _firstDirSector = U32(0x30);
            _miniCutoff = U32(0x38);
            _firstMiniFatSector = U32(0x3C);
            _miniFatSectorCount = U32(0x40);
            var firstDifatSector = U32(0x44);
            var difatSectorCount = U32(0x48);

            if (fatSectorCount == 0 || fatSectorCount > _sectorCount)
                throw new InvalidDataException("Invalid FAT sector count.");
            if (difatSectorCount > _sectorCount)
                throw new InvalidDataException("Invalid DIFAT sector count.");

            _fat = LoadFat(fatSectorCount, firstDifatSector, difatSectorCount);
        }

        /// <summary>Reads the whole content of a root-level stream by name (case-insensitive).</summary>
        public static byte[] ReadStream(byte[] file, string name) => new CompoundFileReader(file).ReadNamedStream(name);

        /// <summary>Parses the container once so several streams can be read from it.</summary>
        public static CompoundFileReader Open(byte[] file) => new(file);

        /// <summary>Reads the whole content of a root-level stream by name (case-insensitive).</summary>
        public byte[] GetStream(string name) => ReadNamedStream(name);

        // [MS-CFB]: version 3 files (512-byte sectors) must ignore the high 32 bits of a stream size.
        private ulong ReadStreamSize(ReadOnlySpan<byte> entry)
        {
            var size = BinaryPrimitives.ReadUInt64LittleEndian(entry[120..]);
            return _sectorSize == 512 ? size & 0xFFFFFFFFUL : size;
        }

        private uint U32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(_file.AsSpan(offset));

        private long SectorOffset(uint sector)
        {
            if (sector >= _sectorCount)
                throw new InvalidDataException("A sector index lies outside the file.");
            return (sector + 1L) * _sectorSize;
        }

        private uint[] LoadFat(uint fatSectorCount, uint firstDifatSector, uint difatSectorCount)
        {
            var fatSectors = new List<uint>();

            for (var i = 0; i < HeaderDifatCount && fatSectors.Count < fatSectorCount; i++)
            {
                var id = U32(0x4C + i * 4);
                if (id == FreeSector)
                    break;
                fatSectors.Add(id);
            }

            var entriesPerDifat = _sectorSize / 4 - 1;
            var difatSector = firstDifatSector;
            for (uint d = 0; fatSectors.Count < fatSectorCount; d++)
            {
                if (difatSector is EndOfChain or FreeSector)
                    break;
                if (d >= difatSectorCount)
                    throw new InvalidDataException("The DIFAT chain is longer than declared.");

                var offset = SectorOffset(difatSector);
                for (var i = 0; i < entriesPerDifat && fatSectors.Count < fatSectorCount; i++)
                {
                    var id = BinaryPrimitives.ReadUInt32LittleEndian(_file.AsSpan((int)(offset + i * 4L)));
                    if (id == FreeSector)
                        break;
                    fatSectors.Add(id);
                }

                difatSector = BinaryPrimitives.ReadUInt32LittleEndian(_file.AsSpan((int)(offset + entriesPerDifat * 4L)));
            }

            if (fatSectors.Count != fatSectorCount)
                throw new InvalidDataException("The FAT is incomplete.");

            // fatSectorCount <= sectorCount <= file size / sectorSize, so this allocation is bounded by the file size.
            var fat = new uint[(long)fatSectors.Count * (_sectorSize / 4)];
            for (var i = 0; i < fatSectors.Count; i++)
            {
                var offset = SectorOffset(fatSectors[i]);
                for (var j = 0; j < _sectorSize / 4; j++)
                    fat[i * (_sectorSize / 4) + j] = BinaryPrimitives.ReadUInt32LittleEndian(_file.AsSpan((int)(offset + j * 4L)));
            }

            return fat;
        }

        /// <summary>Follows a regular FAT chain. At most <paramref name="maxSectors"/> sectors are returned; longer chains are cycles or corrupt.</summary>
        private List<uint> FollowChain(uint[] table, uint start, long maxSectors)
        {
            var sectors = new List<uint>();
            var current = start;
            while (current != EndOfChain)
            {
                if (current > MaxRegularSector || current >= table.Length || current >= _sectorCount)
                    throw new InvalidDataException("A sector chain references an invalid sector.");
                if (sectors.Count >= maxSectors)
                    throw new InvalidDataException("A sector chain is cyclic or longer than the file allows.");

                sectors.Add(current);
                current = table[current];
            }

            return sectors;
        }

        private byte[] ReadRegularChain(uint start, long size)
        {
            if (size < 0 || size > _file.Length)
                throw new InvalidDataException("A stream size exceeds the file length.");

            var needed = (size + _sectorSize - 1) / _sectorSize;
            var chain = FollowChain(_fat, start, _sectorCount);
            if (chain.Count < needed)
                throw new InvalidDataException("A stream chain is shorter than its declared size.");

            var result = new byte[size];
            for (var i = 0; i < needed; i++)
            {
                var offset = SectorOffset(chain[i]);
                var count = (int)Math.Min(_sectorSize, size - i * (long)_sectorSize);
                Buffer.BlockCopy(_file, (int)offset, result, (int)(i * (long)_sectorSize), count);
            }

            return result;
        }

        private byte[] ReadDirectory()
        {
            // Directory chain length is bounded by the sector count; its size is derived from the chain itself.
            var chain = FollowChain(_fat, _firstDirSector, _sectorCount);
            if (chain.Count == 0)
                throw new InvalidDataException("The compound file has no directory.");

            var result = new byte[chain.Count * (long)_sectorSize];
            for (var i = 0; i < chain.Count; i++)
                Buffer.BlockCopy(_file, (int)SectorOffset(chain[i]), result, i * _sectorSize, _sectorSize);
            return result;
        }

        private byte[] ReadNamedStream(string name)
        {
            _directory ??= ReadDirectory();
            var directory = _directory;
            var entryCount = directory.Length / DirectoryEntrySize;

            // Root entry (index 0) holds the mini stream location and the root of the child tree.
            if (directory[66] != 5)
                throw new InvalidDataException("The root directory entry is invalid.");

            var rootChild = BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(76));
            var found = -1;
            var visited = new HashSet<uint>();
            var pending = new Stack<uint>();
            if (rootChild != NoStream)
                pending.Push(rootChild);

            // Walk the red-black tree of root children; the visited set stops loops.
            while (pending.Count > 0 && found < 0)
            {
                var index = pending.Pop();
                if (index >= entryCount)
                    throw new InvalidDataException("A directory entry index is out of range.");
                if (!visited.Add(index))
                    throw new InvalidDataException("The directory tree contains a cycle.");

                var entry = directory.AsSpan((int)(index * (long)DirectoryEntrySize), DirectoryEntrySize);
                var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(entry[64..]);
                if (entry[66] == 2 && nameLength >= 2 && nameLength <= 64 && nameLength % 2 == 0)
                {
                    var entryName = Encoding.Unicode.GetString(entry.Slice(0, nameLength - 2));
                    if (string.Equals(entryName, name, StringComparison.OrdinalIgnoreCase))
                    {
                        found = (int)index;
                        break;
                    }
                }

                var left = BinaryPrimitives.ReadUInt32LittleEndian(entry[68..]);
                var right = BinaryPrimitives.ReadUInt32LittleEndian(entry[72..]);
                if (left != NoStream) pending.Push(left);
                if (right != NoStream) pending.Push(right);
            }

            if (found < 0)
                throw new InvalidDataException($"The stream '{name}' was not found.");

            var target = directory.AsSpan(found * DirectoryEntrySize, DirectoryEntrySize);
            var startSector = BinaryPrimitives.ReadUInt32LittleEndian(target[116..]);
            var size = ReadStreamSize(target);
            if (size > (ulong)_file.Length)
                throw new InvalidDataException("A stream size exceeds the file length.");

            if (size == 0)
                return [];

            if (size >= _miniCutoff)
                return ReadRegularChain(startSector, (long)size);

            return ReadMiniStream(directory, startSector, (long)size);
        }

        private byte[] ReadMiniStream(byte[] directory, uint startMiniSector, long size)
        {
            var rootStart = BinaryPrimitives.ReadUInt32LittleEndian(directory.AsSpan(116));
            var rootSize = ReadStreamSize(directory.AsSpan(0, DirectoryEntrySize));
            if (rootSize > (ulong)_file.Length)
                throw new InvalidDataException("The mini stream size exceeds the file length.");

            var miniStream = ReadRegularChain(rootStart, (long)rootSize);

            // Mini FAT: a regular chain whose entries index 64-byte mini sectors.
            if (_miniFatSectorCount == 0 || _miniFatSectorCount > _sectorCount)
                throw new InvalidDataException("Invalid mini FAT sector count.");
            var miniFatChain = FollowChain(_fat, _firstMiniFatSector, _miniFatSectorCount);
            var entriesPerSector = _sectorSize / 4;
            var miniFat = new uint[miniFatChain.Count * (long)entriesPerSector];
            for (var i = 0; i < miniFatChain.Count; i++)
            {
                var offset = SectorOffset(miniFatChain[i]);
                for (var j = 0; j < entriesPerSector; j++)
                    miniFat[i * entriesPerSector + j] = BinaryPrimitives.ReadUInt32LittleEndian(_file.AsSpan((int)(offset + j * 4L)));
            }

            var miniSectorCount = miniStream.Length / MiniSectorSize;
            var needed = (size + MiniSectorSize - 1) / MiniSectorSize;

            var result = new byte[size];
            var current = startMiniSector;
            for (long i = 0; i < needed; i++)
            {
                if (current > MaxRegularSector || current >= miniFat.Length || current >= miniSectorCount)
                    throw new InvalidDataException("A mini sector chain references an invalid sector.");

                var count = (int)Math.Min(MiniSectorSize, size - i * MiniSectorSize);
                Buffer.BlockCopy(miniStream, (int)(current * (long)MiniSectorSize), result, (int)(i * MiniSectorSize), count);
                current = miniFat[current];

                if (i + 1 < needed && current == EndOfChain)
                    throw new InvalidDataException("A mini stream chain is shorter than its declared size.");
            }

            return result;
        }
    }
}
