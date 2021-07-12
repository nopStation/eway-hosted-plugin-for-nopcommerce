using Nop.Core.Configuration;

namespace Nop.Plugin.Payments.eWayHosted
{
    public class eWayHostedPaymentSettings : ISettings
    {
        public string CustomerId { get; set; }
        public string Username { get; set; }
        public string PaymentPage { get; set; }
        public decimal AdditionalFee { get; set; }
    }
}
