namespace Settlement_Services.Framework.Compatibility
{
    internal interface ICompatibilityQuoteModifier
    {
        int Order { get; }
        void Modify(CompatibilityQuoteContext context);
    }
}
