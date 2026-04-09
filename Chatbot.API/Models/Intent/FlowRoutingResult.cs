namespace Chatbot.API.Models.Intent
{
    public class FlowRoutingResult
    {
        public string FlowType { get; set; } = ChatFlowType.Unknown;

        public bool ShouldUseDeterministicFlow { get; set; }

        public bool ShouldUseRag { get; set; }

        public bool ShouldUseAiFallback { get; set; } = true;

        public string? Reason { get; set; }
    }
}