namespace Nop.Plugin.Payments.eWayHosted
{
    public class TransactionRequestResult
    {
        public bool Result { get; set; }
        public string Uri { get; set; }
        public string Error { get; set; }
    }
}
