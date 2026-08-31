using Settlement_Services.Framework.Dto;
using Settlement_Services.Framework.Workers;

namespace Settlement_Services.Framework.Payment
{
    public interface IServicePaymentProvider
    {
        bool HasEnough(int amount, SettlementServiceRequest request);
        bool TryDebit(int amount, SettlementServiceRequest request, out string errorKey);
        void Refund(int amount, ServiceJobContext ctx);
    }
}
