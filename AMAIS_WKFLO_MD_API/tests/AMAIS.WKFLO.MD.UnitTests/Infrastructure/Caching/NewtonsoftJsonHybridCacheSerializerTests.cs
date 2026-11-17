using System;
using System.Collections.Generic;
using System.Buffers;
using System.Globalization;
using AMAIS.WKFLO.Common.MD.Domain.Caching;
using EFCoreSecondLevelCacheInterceptor;
using Newtonsoft.Json;

namespace AMAIS.WKFLO.MD.UnitTests.Infrastructure.Caching
{
    public class NewtonsoftJsonHybridCacheSerializerTests
    {
        private static readonly Guid SampleGuid = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

        private static EFCachedData RoundTrip(EFCachedData data)
        {
            var serializer = new NewtonsoftJsonHybridCacheSerializer<EFCachedData>();
            var buffer = new ArrayBufferWriter<byte>();

            serializer.Serialize(data, buffer);

            return serializer.Deserialize(new ReadOnlySequence<byte>(buffer.WrittenMemory));
        }

        [Fact]
        public void RoundTrip_PreservesColumnValueTypes()
        {
            var offsetValue = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.FromHours(-3));
            var rows = new EFTableRows { FieldCount = 7 };
            rows.Add(new EFTableRow(new List<object> { SampleGuid, offsetValue, 12.345m, true, DBNull.Value, new byte[] { 1, 2, 3 }, "text" }));
            rows.Add(new EFTableRow(new List<object> { 42, 42L, 1.5d, false, DBNull.Value, new byte[0], "" }));

            var result = RoundTrip(new EFCachedData { TableRows = rows });

            var first = result.TableRows!.Rows[0].Values;
            Assert.Equal(SampleGuid, Guid.Parse(first[0].ToString()!));
            Assert.Equal(offsetValue, DateTimeOffset.Parse(first[1].ToString()!, CultureInfo.InvariantCulture)); // offset kept
            Assert.Equal(12.345m, Assert.IsType<decimal>(first[2]));
            Assert.True(Assert.IsType<bool>(first[3]));
            Assert.True(first[4] is null or DBNull); // the reader treats both as SQL NULL
            Assert.Equal(new byte[] { 1, 2, 3 }, Assert.IsType<byte[]>(first[5]));
            Assert.Equal("text", Assert.IsType<string>(first[6]));

            var second = result.TableRows.Rows[1].Values;
            Assert.Equal(42, Convert.ToInt32(second[0]));
            Assert.Equal(42L, Convert.ToInt64(second[1]));
            Assert.Equal(1.5d, Convert.ToDouble(second[2]));
        }

        [Theory]
        [InlineData("pt-BR")]
        [InlineData("es-ES")]
        [InlineData("en-US")]
        public void CachedRow_ReadsBackThroughTheEfReader_UnderAnyCulture(string cultureName)
        {
            var previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);

            try
            {
                var dateTime = new DateTime(2026, 3, 4, 15, 6, 7, 123, DateTimeKind.Unspecified);
                var offsetValue = new DateTimeOffset(2026, 3, 4, 5, 6, 7, 123, TimeSpan.FromHours(-3));
                var amount = 123456789012.123456m; // decimal(18,6)

                var rows = new EFTableRows { FieldCount = 3 };
                rows.ColumnsInfo.Add(0, new EFTableColumnInfo { Ordinal = 0, Name = "At", DbTypeName = "datetime2", TypeName = "System.DateTime" });
                rows.ColumnsInfo.Add(1, new EFTableColumnInfo { Ordinal = 1, Name = "Offset", DbTypeName = "datetimeoffset", TypeName = "System.DateTimeOffset" });
                rows.ColumnsInfo.Add(2, new EFTableColumnInfo { Ordinal = 2, Name = "Amount", DbTypeName = "decimal", TypeName = "System.Decimal" });
                rows.Add(new EFTableRow(new List<object> { dateTime, offsetValue, amount }));

                var result = RoundTrip(new EFCachedData { TableRows = rows });
                using var reader = new EFTableRowsDataReader(result.TableRows!, new EFCoreSecondLevelCacheSettings());
                Assert.True(reader.Read());

                var restoredDateTime = reader.GetDateTime(0);
                Assert.Equal(dateTime, restoredDateTime);
                Assert.Equal(DateTimeKind.Unspecified, restoredDateTime.Kind);

                var restoredOffset = reader.GetFieldValue<DateTimeOffset>(1);
                Assert.Equal(offsetValue, restoredOffset);
                Assert.Equal(offsetValue.Offset, restoredOffset.Offset);

                Assert.Equal(amount, reader.GetDecimal(2));
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Fact]
        public void RoundTrip_PreservesColumnInfoAndScalarAndNullMarker()
        {
            var rows = new EFTableRows();
            rows.ColumnsInfo.Add(0, new EFTableColumnInfo { Ordinal = 0, Name = "Id", DbTypeName = "int", TypeName = "System.Int32" });

            var result = RoundTrip(new EFCachedData { TableRows = rows, Scalar = 7, IsNull = true });

            Assert.Equal("Id", result.TableRows!.ColumnsInfo[0].Name);
            Assert.True(result.IsNull);
            Assert.NotNull(result.Scalar);
        }

        [Fact]
        public void RoundTrip_PreservesTagList()
        {
            var serializer = new NewtonsoftJsonHybridCacheSerializer<List<string>>();
            var buffer = new ArrayBufferWriter<byte>();

            serializer.Serialize(["a", "b"], buffer);

            Assert.Equal(["a", "b"], serializer.Deserialize(new ReadOnlySequence<byte>(buffer.WrittenMemory)));
        }

        [Fact]
        public void Deserialize_RejectsTypesOutsideTheAllowlist()
        {
            var serializer = new NewtonsoftJsonHybridCacheSerializer<object>();

            foreach (var payload in new[]
            {
                """{"$type":"System.Diagnostics.Process, System.Diagnostics.Process","StartInfo":null}""",
                // Interceptor assembly, but not one of the four payload types.
                """{"$type":"EFCoreSecondLevelCacheInterceptor.EFCachePolicy, EFCoreSecondLevelCacheInterceptor"}""",
                // Allowed generic type with a disallowed argument.
                """{"$type":"System.Collections.Generic.List`1[[System.Uri, System.Private.Uri]], System.Private.CoreLib","$values":[]}"""
            })
            {
                Assert.Throws<JsonSerializationException>(() =>
                    serializer.Deserialize(new ReadOnlySequence<byte>(System.Text.Encoding.UTF8.GetBytes(payload))));
            }
        }
    }
}
