using OpenAIFunctionCall.Extension;
using OpenAIFunctionCall.Interfaces;
using OpenAIFunctionCall.Services;
using System.Text;
using System.Text.Json;

namespace OpenAIFunctionCall.LLM
{
    internal static class OpenAILLM
    {
        public static async IAsyncEnumerable<string> GetStreamingResponseAsync(OpenaiApiHttpClient client, string prompt, IFunctionTools tools)
        {
            string llm = "gpt-4o-2024-08-06";

            var requestPayload = new
            {
                model = llm,
                messages = new object[] {
                    new { role = "system", content = "You are an assistant that can call tools." },
                    new { role = "user", content = prompt }
                },
                tools = tools.Definition()
            };

            var content = new StringContent(JsonSerializer.Serialize(requestPayload), Encoding.UTF8, "application/json");
            using var doc = await client.PostPayloadWithReturnJsonDocumentAsync(string.Empty, content);
            var root = doc.RootElement;
            var choices = root.GetProperty("choices");
            var choicesArray = choices.EnumerateArray();

            bool hasToolCall = false;
            List<JsonElement> followUpMessages = new List<JsonElement>();
            foreach (var choice in choicesArray)
            {
                var choiceMsg = choice.GetProperty("message");
                string? finishReason = choice.GetProperty("finish_reason").GetString();
                if (string.Compare(finishReason, "tool_calls", true) == 0)
                {
                    hasToolCall = true;
                    followUpMessages.Add(choiceMsg);
                    var toolCalls = choiceMsg.GetProperty("tool_calls").EnumerateArray();
                    foreach (var toolCall in toolCalls)
                    {
                        var function = toolCall.GetProperty("function");
                        string? funcName = function.GetProperty("name").GetString();
                        string? argsJson = function.GetProperty("arguments").GetString();

                        yield return $"Model requested function: {funcName} with arguments: {argsJson}.";

                        var funcResult = await tools.Implementation(funcName, argsJson);
                        string? toolCallId = toolCall.GetProperty("id").GetString();

                        var toolMsg = new
                        {
                            role = "tool",
                            tool_call_id = toolCallId,
                            content = JsonSerializer.Serialize(funcResult)
                        };
                        followUpMessages.Add(JsonDocument.Parse(JsonSerializer.Serialize(toolMsg)).RootElement);
                    }
                }
                else if (string.Compare(finishReason, "stop", true) == 0)
                {
                    string? msgContent = choiceMsg.GetProperty("content").GetString();
                    yield return msgContent ?? "Model returned no answer.";
                }
            }

            if (hasToolCall)
            {
                yield return "Following up ...";

                string followupMsg = JsonSerializer.Serialize(followUpMessages.ToArray()).Replace("u0022", "\"");

                string followupPayload = $"{{ \"model\": \"{llm}\", \"messages\": {followupMsg} }}";
                var followupContent = new StringContent(followupPayload, Encoding.UTF8, "application/json");
                using var followupResultDoc = await client.PostPayloadWithReturnJsonDocumentAsync(string.Empty, followupContent);

                var followupResultRoot = followupResultDoc.RootElement;
                var followupChoices = followupResultRoot.GetProperty("choices");
                var followupChoicesArray = followupChoices.EnumerateArray();

                foreach (var choice in followupChoicesArray)
                {
                    var choiceMsg = choice.GetProperty("message");
                    string? finishReason = choice.GetProperty("finish_reason").GetString();
                    if (string.Compare(finishReason, "stop", true) == 0)
                    {
                        string? msgContent = choiceMsg.GetProperty("content").GetString();
                        yield return msgContent ?? "Model returned no answer.";
                    }
                }
            }
        }
    }
}
