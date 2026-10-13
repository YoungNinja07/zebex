namespace Zebex.Simulator;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length == 1 && args[0] == "--create-com-pair") Application.Run(new ComPairSetupForm());
        else if (args.Length == 1 && args[0] is "--install-suz-bridge" or "--restore-suz-bridge") Application.Run(new SuzBridgeSetupForm(args[0] == "--restore-suz-bridge"));
        else Application.Run(new MainForm());
    }
}
