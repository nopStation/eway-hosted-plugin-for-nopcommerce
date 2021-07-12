using Microsoft.AspNetCore.Mvc;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Payments.eWayHosted.Components
{
    [ViewComponent(Name = "PaymenteWayHosted")]
    public class PaymenteWayHostedViewComponent : NopViewComponent
    {
        public IViewComponentResult Invoke()
        {
            return View("~/Plugins/Payments.eWayHosted/Views/PaymentInfo.cshtml");
        }
    }
}
