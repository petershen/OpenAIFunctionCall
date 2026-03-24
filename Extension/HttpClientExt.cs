using System.Text.Json;

namespace OpenAIFunctionCall.Extension
{
    internal static class HttpClientExt
    {
        public static async Task<JsonDocument> GetJsonDocumentAsync(this HttpClient client, string requestUri)
        {
            using var response = await client.GetAsync(requestUri);
            response.EnsureSuccessStatusCode();
            return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        }

        public static async Task<JsonDocument> PostPayloadWithReturnJsonDocumentAsync(this HttpClient client, string requestUri, StringContent payload)
        {
            var response = await client.PostAsync(requestUri, payload);
            response.EnsureSuccessStatusCode();
            using var stream = await response.Content.ReadAsStreamAsync();
            return await JsonDocument.ParseAsync(stream);
        }
    }
}
