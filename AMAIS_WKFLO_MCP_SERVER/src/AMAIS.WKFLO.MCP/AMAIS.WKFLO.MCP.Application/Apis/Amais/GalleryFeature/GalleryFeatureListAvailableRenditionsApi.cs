using AMAIS.WKFLO.MCP.Application.Apis.Amais.GalleryFeature.Models;
using Flurl.Http;
using Newtonsoft.Json;

namespace AMAIS.WKFLO.MCP.Application.Apis.Amais.GalleryFeature
{
    public partial class GalleryFeatureApi
    {
        /// <summary>
        /// Maximum number of rendition IDs sent per CMS request. CMS versions that support
        /// <c>renditionIds</c> but still cap results with <c>Take(10)</c> would otherwise drop IDs.
        /// </summary>
        internal const int RenditionIdsBatchSize = 10;

        /// <summary>
        /// Sent as <c>searchTerms</c> when only rendition IDs are requested. CMS versions that support
        /// <c>renditionIds</c> still mark <c>searchTerms</c> as required (400 when blank); newer CMS versions
        /// ignore it on the ID path. Older CMS versions ignore <c>renditionIds</c> and run a keyword search,
        /// whose unrelated results are filtered out below.
        /// </summary>
        internal const string RenditionIdsPlaceholderSearchTerms = "image";

        private const string ListAvailableRenditionsPath = "/api/v1/galleryfeature/available/rendition/list";

        public async Task<ListAvailableGalleryFeatureRenditionsResult> ListAvailableGalleryFeatureRenditionsAsync(string searchTerms, IEnumerable<int> renditionIds = null)
        {
            var requestedIds = (renditionIds ?? [])
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            if (requestedIds.Count == 0)
            {
                return await PostListAvailableRenditionsAsync(new { searchTerms });
            }

            var effectiveSearchTerms = string.IsNullOrWhiteSpace(searchTerms) ? RenditionIdsPlaceholderSearchTerms : searchTerms;
            var returnedRenditions = new List<AvailableGalleryFeatureRenditionsData>();

            foreach (var batch in requestedIds.Chunk(RenditionIdsBatchSize))
            {
                var batchResult = await PostListAvailableRenditionsAsync(new
                {
                    searchTerms = effectiveSearchTerms,
                    renditionIds = batch
                });

                if (batchResult == null || batchResult.IsError)
                {
                    return batchResult;
                }

                returnedRenditions.AddRange(batchResult.Data?.Renditions ?? []);
            }

            // Older CMS versions ignore renditionIds and return keyword matches, so only keep the requested IDs.
            var renditionsById = returnedRenditions
                .Where(r => r != null)
                .GroupBy(r => r.RenditionId)
                .ToDictionary(g => g.Key, g => g.First());

            return new ListAvailableGalleryFeatureRenditionsResult
            {
                ResultType = 200,
                IsError = false,
                Data = new ListAvailableGalleryFeatureImagesData
                {
                    Renditions = [.. requestedIds.Where(renditionsById.ContainsKey).Select(id => renditionsById[id])],
                    NotFoundRenditionIds = [.. requestedIds.Where(id => !renditionsById.ContainsKey(id))]
                }
            };
        }

        private async Task<ListAvailableGalleryFeatureRenditionsResult> PostListAvailableRenditionsAsync(object postBody)
        {
            IFlurlResponse response = await ApiClient.CallApiAsync(ListAvailableRenditionsPath, HttpMethod.Post, postBody: postBody);

            if (((int)response.StatusCode) >= 400)
            {
                var error = await response.ResponseMessage.Content.ReadAsStringAsync();
                return new ListAvailableGalleryFeatureRenditionsResult
                {
                    ResultType = (int)response.StatusCode,
                    Data = null,
                    IsError = true,
                    ErrorMessage = "Error calling ListAvailableGalleryFeatureRenditionsAsync: " + error
                };
            }

            var content = await response.ResponseMessage.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<ListAvailableGalleryFeatureRenditionsResult>(content);
        }
    }
}
