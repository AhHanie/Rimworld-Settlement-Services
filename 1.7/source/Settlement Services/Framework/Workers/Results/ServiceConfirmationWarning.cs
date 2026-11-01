namespace Settlement_Services.Framework.Workers.Results
{
    public class ServiceConfirmationWarning
    {
        public string Text { get; }
        public bool Destructive { get; }

        public ServiceConfirmationWarning(string text, bool destructive)
        {
            Text = text;
            Destructive = destructive;
        }
    }
}
