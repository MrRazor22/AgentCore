using AgentCore.LLMCore.Client;

namespace AgentCore.Tokens
{
    public interface ITokenEstimator
    {
        int Estimate(LLMRequestBase request);
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
    }
}
