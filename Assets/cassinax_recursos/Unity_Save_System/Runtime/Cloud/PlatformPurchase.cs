// Cassinax Unity System Save - v1.0.0
// Compras comprovadas pela plataforma.
using System.Collections.Generic;
namespace cassinax.savesystem
{
    public struct PlatformPurchaseReceipt
    {
        public string productId, transactionId, source;
        public bool owned;
        public PlatformPurchaseReceipt(string productId, string transactionId, string source, bool owned = true)
        { this.productId = productId; this.transactionId = transactionId; this.source = source; this.owned = owned; }
    }
    public interface IPlatformPurchaseAdapter { IEnumerable<PlatformPurchaseReceipt> GetOwnedPurchases(); }
}
