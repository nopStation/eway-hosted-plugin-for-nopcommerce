using System;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Plugin.Payments.eWayHosted.Models;
using Nop.Services.Configuration;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;
using Nop.Services.Security;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Nop.Plugin.Payments.eWayHosted.Controllers
{
    public class PaymenteWayHostedController : BasePaymentController
    {
        private readonly eWayHostedPaymentSettings _eWayHostedPaymentSettings;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly IOrderService _orderService;
        private readonly IPermissionService _permissionService;
        private readonly ISettingService _settingService;
        private readonly IPaymentPluginManager _paymentPluginManager;

        public PaymenteWayHostedController(
            eWayHostedPaymentSettings eWayHostedPaymentSettings,
            IOrderProcessingService orderProcessingService,
            IOrderService orderService,
            IPermissionService permissionService,
            ISettingService settingService,
            IPaymentPluginManager paymentPluginManager)
        {
            _eWayHostedPaymentSettings = eWayHostedPaymentSettings;
            _orderProcessingService = orderProcessingService;
            _orderService = orderService;
            _permissionService = permissionService;
            _settingService = settingService;
            _paymentPluginManager = paymentPluginManager;
        }

        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public async Task<IActionResult> Configure()
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            var model = new ConfigurationModel
            {
                CustomerId = _eWayHostedPaymentSettings.CustomerId,
                Username = _eWayHostedPaymentSettings.Username,
                PaymentPage = _eWayHostedPaymentSettings.PaymentPage,
                AdditionalFee = _eWayHostedPaymentSettings.AdditionalFee
            };

            return View("~/Plugins/Payments.eWayHosted/Views/Configure.cshtml", model);
        }

        [HttpPost]
        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public async Task<IActionResult> Configure(ConfigurationModel model)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            //save settings
            _eWayHostedPaymentSettings.CustomerId = model.CustomerId;
            _eWayHostedPaymentSettings.Username = model.Username;
            _eWayHostedPaymentSettings.PaymentPage = model.PaymentPage;
            _eWayHostedPaymentSettings.AdditionalFee = model.AdditionalFee;
            await _settingService.SaveSettingAsync(_eWayHostedPaymentSettings);

            return RedirectToAction("Configure");
        }

        public async Task<IActionResult> MerchantReturn(IFormCollection form)
        {
            var processor =
                await _paymentPluginManager.LoadPluginBySystemNameAsync("Payments.eWayHosted") as eWayHostedPaymentProcessor;
            if (processor == null ||
                !_paymentPluginManager.IsPluginActive(processor) || !processor.PluginDescriptor.Installed)
                throw new NopException("eWayHosted module cannot be loaded");

            var accessPaymentCode = string.Empty;
            if (form.ContainsKey("AccessPaymentCode"))
                accessPaymentCode = form["AccessPaymentCode"];

            //get the result of the transaction based on the unique payment code
            var validationResult = processor.CheckAccessCode(accessPaymentCode);

            if (!string.IsNullOrEmpty(validationResult.ErrorMessage))
            {
                //failed
                return RedirectToAction("Index", "Home", new { area = "" });
            }

            if (string.IsNullOrEmpty(validationResult.TrxnStatus) ||
                !validationResult.TrxnStatus.ToLower().Equals("true"))
            {
                //failed
                return RedirectToAction("Index", "Home", new { area = "" });
            }
            var orderId = Convert.ToInt32(validationResult.MerchnatOption1);
            var order = await _orderService.GetOrderByIdAsync(orderId);
            if (order == null) return RedirectToAction("Index", "Home", new { area = "" });

            if (_orderProcessingService.CanMarkOrderAsPaid(order))
            {
                await _orderProcessingService.MarkOrderAsPaidAsync(order);
            }

            return RedirectToRoute("CheckoutCompleted", new { orderId = order.Id });
        }
    }
}