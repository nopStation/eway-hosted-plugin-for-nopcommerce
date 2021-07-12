using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Xml;
using Microsoft.AspNetCore.Http;
using Nop.Core;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Services.Plugins;
using Nop.Services.Configuration;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Payments;
using System.Threading.Tasks;
using Nop.Services.Common;

namespace Nop.Plugin.Payments.eWayHosted
{
    /// <summary>
    /// eWayHosted payment processor
    /// </summary>
    public class eWayHostedPaymentProcessor : BasePlugin, IPaymentMethod
    {
        #region Fields

        private readonly CurrencySettings _currencySettings;
        private readonly eWayHostedPaymentSettings _eWayHostedPaymentSettings;
        private readonly ICurrencyService _currencyService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILocalizationService _localizationService;
        private readonly ISettingService _settingService;
        private readonly IWebHelper _webHelper;
        private readonly IAddressService _addressService;
        private readonly IStateProvinceService _stateProvinceService;
        private readonly ICountryService _countryService;

        #endregion

        #region Ctor

        public eWayHostedPaymentProcessor(CurrencySettings currencySettings,
            eWayHostedPaymentSettings eWayHostedPaymentSettings,
            ICurrencyService currencyService,
            IHttpContextAccessor httpContextAccessor,
            ILocalizationService localizationService,
            ISettingService settingService,
            IWebHelper webHelper,
            IAddressService addressService,
            IStateProvinceService stateProvinceService,
            ICountryService countryService)
        {
            _currencySettings = currencySettings;
            _eWayHostedPaymentSettings = eWayHostedPaymentSettings;
            _currencyService = currencyService;
            _httpContextAccessor = httpContextAccessor;
            _localizationService = localizationService;
            _settingService = settingService;
            _webHelper = webHelper;
            _addressService = addressService;
            _stateProvinceService = stateProvinceService;
            _countryService = countryService;
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Format the string needed to post to the Generate page
        /// </summary>
        /// <param name="fieldName">eWAY Parameter Name</param>
        /// <param name="value">Value of Parameter</param>
        /// <returns>Formated value for the URL</returns>
        private string Format(string fieldName, string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : $"&{fieldName}={value}";
        }

        /// <summary>
        /// Parse the result of the transaction request and save the appropriate fields in an object to be used later
        /// </summary>
        /// <param name="xml"></param>
        /// <returns></returns>
        private TransactionRequestResult ParseRequestResults(string xml)
        {
            var sr = new StringReader(xml);
            var xtr = new XmlTextReader(sr)
            {
                XmlResolver = null,
                WhitespaceHandling = WhitespaceHandling.None
            };

            // get the root node
            xtr.Read();

            var res = new TransactionRequestResult();

            if ((xtr.NodeType != XmlNodeType.Element) || (xtr.Name != "TransactionRequest"))
                return res;

            while (xtr.Read())
            {
                if ((xtr.NodeType != XmlNodeType.Element) || xtr.IsEmptyElement)
                    continue;

                var currentNode = xtr.Name;
                xtr.Read();
                if (xtr.NodeType != XmlNodeType.Text)
                    continue;

                switch (currentNode)
                {
                    case "Result":
                        res.Result = bool.Parse(xtr.Value);
                        break;

                    case "URI":
                        res.Uri = xtr.Value;
                        break;

                    case "Error":
                        res.Error = xtr.Value;
                        break;
                }
            }

            return res;
        }

        /// <summary>
        /// Parse the XML Returned and save all the values to be displayed to user
        /// </summary>
        /// <param name="resultXml">XML of the transaction result</param>
        private ValdiationRequestResult ParseXmlResult(string resultXml)
        {
            var result = new ValdiationRequestResult();
            string currentNode;
            var sr = new StringReader(resultXml);
            var xtr = new XmlTextReader(sr)
            {
                XmlResolver = null,
                WhitespaceHandling = WhitespaceHandling.None
            };

            // get the root node
            xtr.Read();

            if ((xtr.NodeType == XmlNodeType.Element) && (xtr.Name == "TransactionResponse"))
            {
                while (xtr.Read())
                {
                    if ((xtr.NodeType != XmlNodeType.Element) || xtr.IsEmptyElement)
                        continue;

                    currentNode = xtr.Name;
                    xtr.Read();
                    if (xtr.NodeType != XmlNodeType.Text)
                        continue;

                    switch (currentNode)
                    {
                        case "AuthCode":
                            result.AuthCode = xtr.Value;
                            break;
                        case "ResponseCode":
                            result.ResponseCode = xtr.Value;
                            break;
                        case "ReturnAmount":
                            result.ReturnAmount = xtr.Value;
                            break;
                        case "TrxnStatus":
                            result.TrxnStatus = xtr.Value;
                            break;
                        case "TrxnNumber":
                            result.TrxnNumber = xtr.Value;
                            break;
                        case "MerchantOption1":
                            result.MerchnatOption1 = xtr.Value;
                            break;
                        case "MerchantOption2":
                            result.MerchnatOption2 = xtr.Value;
                            break;
                        case "MerchantOption3":
                            result.MerchnatOption3 = xtr.Value;
                            break;
                        case "MerchantInvoice":
                            result.ReferenceInvoice = xtr.Value;
                            break;
                        case "MerchantReference":
                            result.ReferenceNumber = xtr.Value;
                            break;
                        case "TrxnResponseMessage":
                            result.TrxnResponseMessage = xtr.Value;
                            break;
                        case "ErrorMessage":
                            result.ErrorMessage = xtr.Value;
                            break;
                    }
                }
            }
            else if ((xtr.NodeType == XmlNodeType.Element) && (xtr.Name == "TransactionRequest"))
            {
                while (xtr.Read())
                {
                    if ((xtr.NodeType != XmlNodeType.Element) || xtr.IsEmptyElement)
                        continue;

                    currentNode = xtr.Name;
                    xtr.Read();
                    if (xtr.NodeType != XmlNodeType.Text)
                        continue;

                    if (currentNode == "Error")
                    {
                        result.ErrorMessage = xtr.Value;
                    }
                }
            }
            return result;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Process a payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>Process payment result</returns>
        public Task<ProcessPaymentResult> ProcessPaymentAsync(ProcessPaymentRequest processPaymentRequest)
        {
            var result = new ProcessPaymentResult { NewPaymentStatus = PaymentStatus.Pending };
            return Task.FromResult(result);
        }

        /// <summary>
        /// Post process payment (used by payment gateways that require redirecting to a third-party URL)
        /// </summary>
        /// <param name="postProcessPaymentRequest">Payment info required for an order processing</param>
        public async Task PostProcessPaymentAsync(PostProcessPaymentRequest postProcessPaymentRequest)
        {
            var strPost = "CustomerID=" + _eWayHostedPaymentSettings.CustomerId;
            strPost += Format("UserName", _eWayHostedPaymentSettings.Username);
            //send amounts to the generator in DOLLAR FORM. ie 10.05
            strPost += Format("Amount", postProcessPaymentRequest.Order.OrderTotal.ToString("0.00", CultureInfo.InvariantCulture));
            strPost += Format("Currency", (await _currencyService.GetCurrencyByIdAsync(_currencySettings.PrimaryStoreCurrencyId)).CurrencyCode);

            // supported languages: 
            // "EN" - English
            // "FR" - French
            // "DE" - German
            // "ES" - Spanish
            // "NL" - Dutch
            var billingAddress = await _addressService.GetAddressByIdAsync(postProcessPaymentRequest.Order.BillingAddressId);
            strPost += Format("Language", "EN");
            strPost += Format("CustomerFirstName", billingAddress.FirstName);
            strPost += Format("CustomerLastName", billingAddress.LastName);
            strPost += Format("CustomerAddress", billingAddress.Address1);
            strPost += Format("CustomerCity", billingAddress.City);

            var stateProvince = await _stateProvinceService.GetStateProvinceByIdAsync(billingAddress.StateProvinceId ?? 0);
            var country = await _countryService.GetCountryByIdAsync(billingAddress.CountryId ?? 0);
            strPost += Format("CustomerState", stateProvince?.Name ?? "");
            strPost += Format("CustomerPostCode", billingAddress.ZipPostalCode);
            strPost += Format("CustomerCountry", country?.Name ?? string.Empty);
            strPost += Format("CustomerEmail", billingAddress.Email);
            strPost += Format("CustomerPhone", billingAddress.PhoneNumber);
            strPost += Format("InvoiceDescription", postProcessPaymentRequest.Order.Id.ToString());
            strPost += Format("CancelURL", _webHelper.GetStoreLocation() + "Plugins/PaymenteWayHosted/MerchantReturn");
            strPost += Format("ReturnUrl", _webHelper.GetStoreLocation() + "Plugins/PaymenteWayHosted/MerchantReturn");

            strPost += Format("MerchantReference", postProcessPaymentRequest.Order.Id.ToString());
            strPost += Format("MerchantInvoice", postProcessPaymentRequest.Order.Id.ToString());
            strPost += Format("MerchantOption1", postProcessPaymentRequest.Order.Id.ToString());

            var url = _eWayHostedPaymentSettings.PaymentPage + "Request?" + strPost;

            var objRequest = (HttpWebRequest)WebRequest.Create(url);
            objRequest.Method = WebRequestMethods.Http.Get;

            var objResponse = (HttpWebResponse)objRequest.GetResponse();

            //get the response from the transaction generate page
            string resultXml;

            using (var sr = new StreamReader(objResponse.GetResponseStream()))
            {
                resultXml = sr.ReadToEnd();
                // Close and clean up the StreamReader
                sr.Close();
            }

            //parse the result message
            var resultObj = ParseRequestResults(resultXml);

            if (resultObj.Result)
            {
                //redirect the user to the payment page
                _httpContextAccessor.HttpContext.Response.Redirect(resultObj.Uri);
            }
            else
            {
                throw new NopException(resultObj.Error);
            }
        }

        /// <summary>
        /// Returns a value indicating whether payment method should be hidden during checkout
        /// </summary>
        /// <param name="cart">Shoping cart</param>
        /// <returns>true - hide; false - display.</returns>
        public Task<bool> HidePaymentMethodAsync(IList<ShoppingCartItem> cart)
        {
            //you can put any logic here
            //for example, hide this payment method if all products in the cart are downloadable
            //or hide this payment method if current customer is from certain country
            return Task.FromResult(false);
        }

        /// <summary>
        /// Gets additional handling fee
        /// </summary>
        /// <param name="cart">Shoping cart</param>
        /// <returns>Additional handling fee</returns>
        public Task<decimal> GetAdditionalHandlingFeeAsync(IList<ShoppingCartItem> cart)
        {
            return Task.FromResult(_eWayHostedPaymentSettings.AdditionalFee);
        }

        /// <summary>
        /// Captures payment
        /// </summary>
        /// <param name="capturePaymentRequest">Capture payment request</param>
        /// <returns>Capture payment result</returns>
        public Task<CapturePaymentResult> CaptureAsync(CapturePaymentRequest capturePaymentRequest)
        {
            var result = new CapturePaymentResult();
            result.AddError("Capture method not supported");
            return Task.FromResult(result);
        }

        /// <summary>
        /// Refunds a payment
        /// </summary>
        /// <param name="refundPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public Task<RefundPaymentResult> RefundAsync(RefundPaymentRequest refundPaymentRequest)
        {
            var result = new RefundPaymentResult();
            result.AddError("Refund method not supported");
            return Task.FromResult(result);
        }

        /// <summary>
        /// Voids a payment
        /// </summary>
        /// <param name="voidPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public Task<VoidPaymentResult> VoidAsync(VoidPaymentRequest voidPaymentRequest)
        {
            var result = new VoidPaymentResult();
            result.AddError("Void method not supported");
            return Task.FromResult(result);
        }

        /// <summary>
        /// Process recurring payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>Process payment result</returns>
        public Task<ProcessPaymentResult> ProcessRecurringPaymentAsync(ProcessPaymentRequest processPaymentRequest)
        {
            var result = new ProcessPaymentResult();
            result.AddError("Recurring payment not supported");
            return Task.FromResult(result);
        }

        /// <summary>
        /// Cancels a recurring payment
        /// </summary>
        /// <param name="cancelPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public Task<CancelRecurringPaymentResult> CancelRecurringPaymentAsync(CancelRecurringPaymentRequest cancelPaymentRequest)
        {
            var result = new CancelRecurringPaymentResult();
            result.AddError("Recurring payment not supported");
            return Task.FromResult(result);
        }

        /// <summary>
        /// Gets a value indicating whether customers can complete a payment after order is placed but not completed (for redirection payment methods)
        /// </summary>
        /// <param name="order">Order</param>
        /// <returns>Result</returns>
        public Task<bool> CanRePostProcessPaymentAsync(Order order)
        {
            if (order == null)
                throw new ArgumentNullException(nameof(order));

            //eWayHosted is the redirection payment method
            //It also validates whether order is also paid (after redirection) so customers will not be able to pay twice
            
            //payment status should be Pending
            if (order.PaymentStatus != PaymentStatus.Pending)
                return Task.FromResult(false);

            //let's ensure that at least 1 minute passed after order is placed
            return Task.FromResult(!((DateTime.UtcNow - order.CreatedOnUtc).TotalMinutes < 1));
        }

        public override string GetConfigurationPageUrl()
        {
            return $"{_webHelper.GetStoreLocation()}Admin/PaymenteWayHosted/Configure";
        }

        public string GetPublicViewComponentName()
        {
            return "PaymenteWayHosted";
        }

        public Task<IList<string>> ValidatePaymentFormAsync(IFormCollection form)
        {
            var warnings = new List<string>();
            return Task.FromResult<IList<string>>(warnings);
        }

        public Task<ProcessPaymentRequest> GetPaymentInfoAsync(IFormCollection form)
        {
            var paymentInfo = new ProcessPaymentRequest();
            return Task.FromResult(paymentInfo);
        }

        public override async Task InstallAsync()
        {
            var settings = new eWayHostedPaymentSettings()
            {
                CustomerId = "87654321",
                Username = "TestAccount",
                PaymentPage = "https://nz.ewaygateway.com/ ",
                AdditionalFee = 0
            };
            await _settingService.SaveSettingAsync(settings);

            //locales
            await  _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.eWayHosted.RedirectionTip", "You will be redirected to eWay site to complete the order.");
            await  _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.eWayHosted.CustomerId", "Customer ID");
            await  _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.eWayHosted.CustomerId.Hint", "Enter customer ID.");
            await  _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.eWayHosted.Username", "Username");
            await  _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.eWayHosted.Username.Hint", "Enter username.");
            await  _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.eWayHosted.PaymentPage", "Payment page");
            await  _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.eWayHosted.PaymentPage.Hint", "Enter payment page.");
            await  _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.eWayHosted.AdditionalFee", "Additional fee");
            await  _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.eWayHosted.AdditionalFee.Hint", "Enter additional fee to charge your customers.");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.eWayHosted.PaymentMethodDescription", "You will be redirected to eWay site to complete the order.");

            await base.InstallAsync();
        }

        public override async Task UninstallAsync()
        {
            await _settingService.DeleteSettingAsync<eWayHostedPaymentSettings>();

            //locales
             await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.eWayHosted.RedirectionTip");
             await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.eWayHosted.CustomerId");
             await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.eWayHosted.CustomerId.Hint");
             await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.eWayHosted.Username");
             await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.eWayHosted.Username.Hint");
             await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.eWayHosted.PaymentPage");
             await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.eWayHosted.PaymentPage.Hint");
             await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.eWayHosted.AdditionalFee");
             await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.eWayHosted.AdditionalFee.Hint");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.eWayHosted.PaymentMethodDescription");

            await base.UninstallAsync();
        }

        /// <summary>
        /// Procedure to check the 64 character access payment code
        /// for security
        /// </summary>
        /// <param name="accessPaymentCode">64 char code</param>
        /// <returns>true if found; false if not found</returns>
        public ValdiationRequestResult CheckAccessCode(string accessPaymentCode)
        {
            //POST to Payment gateway the access code returned
            var strPost = "CustomerID=" + _eWayHostedPaymentSettings.CustomerId;
            strPost += Format("AccessPaymentCode", accessPaymentCode);
            strPost += Format("UserName", _eWayHostedPaymentSettings.Username);

            var url = _eWayHostedPaymentSettings.PaymentPage + "Result?" + strPost;

            var objRequest = (HttpWebRequest)WebRequest.Create(url);
            objRequest.Method = WebRequestMethods.Http.Get;
            string resultXml;

            try
            {
                var objResponse = (HttpWebResponse)objRequest.GetResponse();

                //get the response from the transaction generate page
                using (var sr = new StreamReader(objResponse.GetResponseStream()))
                {
                    resultXml = sr.ReadToEnd();
                    // Close and clean up the StreamReader
                    sr.Close();
                }
            }
            catch (Exception exc)
            {
                return new ValdiationRequestResult
                {
                    ErrorMessage = exc.Message
                };
            }

            //parse the results save the values
            return ParseXmlResult(resultXml);
        }

        #endregion

        #region Properies

        /// <summary>
        /// Gets a value indicating whether capture is supported
        /// </summary>
        public bool SupportCapture
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Gets a value indicating whether partial refund is supported
        /// </summary>
        public bool SupportPartiallyRefund
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Gets a value indicating whether refund is supported
        /// </summary>
        public bool SupportRefund
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Gets a value indicating whether void is supported
        /// </summary>
        public bool SupportVoid
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Gets a recurring payment type of payment method
        /// </summary>
        public RecurringPaymentType RecurringPaymentType
        {
            get
            {
                return RecurringPaymentType.NotSupported;
            }
        }

        /// <summary>
        /// Gets a payment method type
        /// </summary>
        public PaymentMethodType PaymentMethodType
        {
            get
            {
                return PaymentMethodType.Redirection;
            }
        }

        /// <summary>
        /// Gets a value indicating whether we should display a payment information page for this plugin
        /// </summary>
        public bool SkipPaymentInfo
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a payment method description that will be displayed on checkout pages in the public store
        /// </summary>
        public async Task<string> GetPaymentMethodDescriptionAsync()
        {
            return await _localizationService.GetResourceAsync("Plugins.Payments.eWayHosted.PaymentMethodDescription");
        }

        #endregion
    }
}
