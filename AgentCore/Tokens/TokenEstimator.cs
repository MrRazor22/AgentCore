using AgentCore.Chat;
using AgentCore.LLMCore.Client;

namespace AgentCore.Tokens
{
    public interface ITokenEstimator
    {
        int Estimate(LLMRequestBase request);
        int Estimate(LLMResponseBase response, string? model = null);
        int Estimate(Conversation convo, string? model = null);
    }

    public sealed class TokenEstimator : ITokenEstimator
    {
        private readonly ITokenizer _tokenizer;
        public TokenEstimator(ITokenizer tokenizer)
        {
            _tokenizer = tokenizer;
        }

        public int Estimate(LLMRequestBase request)
        {
            string payload = request.ToSerializablePayload();
            return _tokenizer.Count(payload, request.Model);
        }
        public int Estimate(LLMResponseBase response, string? model = null)
        {
            string payload = response.ToSerializablePayload();
            return _tokenizer.Count(payload, model);
        }
        public int Estimate(Conversation convo, string? model = null)
        {
            string payload = convo.ToJson();
            return _tokenizer.Count(payload, model);
        }
    }
}
