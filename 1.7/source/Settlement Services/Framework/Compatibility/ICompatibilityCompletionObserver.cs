namespace Settlement_Services.Framework.Compatibility
{
    internal interface ICompatibilityCompletionObserver
    {
        void OnCompleted(CompatibilityCompletionContext context);
    }
}
