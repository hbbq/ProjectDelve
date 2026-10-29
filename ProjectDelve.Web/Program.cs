using ProjectDelve.Web;

if (args.Contains("--promote-campaign", StringComparer.Ordinal))
{
    Environment.ExitCode = LibraryPromotion.Command(args);
    return;
}

var app = PlaytestHost.Build(args);
app.Run();
