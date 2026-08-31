using System;
using Verse;

namespace Settlement_Services.Framework.Compat
{
    public static class SettlementServicesModCompat
    {
        public static Type ResolveOptionalType(string typeName, string namespaceHint = null)
        {
            Type type = GenTypes.GetTypeInAnyAssembly(typeName, namespaceHint);
            if (type == null)
                SupportLog.Info($"Optional integration type '{typeName}' not found; a related feature will stay disabled.");
            return type;
        }
    }
}
