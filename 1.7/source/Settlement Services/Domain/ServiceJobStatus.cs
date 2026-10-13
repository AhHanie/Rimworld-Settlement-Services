namespace Settlement_Services.Domain
{
    public enum ServiceJobStatus
    {
        Drafted,
        Quoted,
        Reserved,
        Active,
        AwaitingCollection,
        Completed,
        Collected,
        Cancelled,
        Failed
    }
}
