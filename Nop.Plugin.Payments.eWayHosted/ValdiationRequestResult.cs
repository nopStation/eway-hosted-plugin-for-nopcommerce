namespace Nop.Plugin.Payments.eWayHosted
{
    public class ValdiationRequestResult
    {
        public string AuthCode { get; set; }
        public string ResponseCode { get; set; }
        public string ReturnAmount { get; set; }
        public string TrxnNumber { get; set; }
        public string TrxnStatus { get; set; }
        public string MerchnatOption1 { get; set; }
        public string MerchnatOption2 { get; set; }
        public string MerchnatOption3 { get; set; }
        public string ReferenceNumber { get; set; }
        public string ReferenceInvoice { get; set; }
        public string TrxnResponseMessage { get; set; }
        public string ErrorMessage { get; set; }
    }
}
