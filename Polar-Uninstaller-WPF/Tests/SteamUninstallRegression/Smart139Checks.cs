using PolarUninstallerWpf.Services;

internal static class Smart139Checks
{
    internal static int Run()
    {
        var root=Path.Combine(AppContext.BaseDirectory,"fixtures","smart139-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);int passed=0;
        void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);passed++;}
        try
        {
            string executable=Environment.ProcessPath!;
            var app=new InstalledProgram("Synthetic Capability Fixture","Synthetic Publisher","1.0",root);
            SmartUninstallDecision Decide(InstalledProgram p)=>SmartUninstallSelectionService.Evaluate(p,new[]{p});
            var official=Decide(app with{Uninstall="\""+executable+"\" --fixture-never-executed"});
            Check(official.DeepEnabled&&official.UsesOfficialUninstaller&&!official.GamesEnabled,"Unified Deep Uninstall uses a verified official stage internally");
            var managed=Decide(app with{RequiresOfficialRemoval=true,Uninstall="\""+executable+"\""});
            Check(managed.DeepEnabled&&managed.UsesOfficialUninstaller,"Windows-managed application uses unified Deep with its supported official command");
            Check(Decide(app).DeepEnabled,"Application without official command uses existing safe direct-removal validation");
            Check(Decide(app with{RequiresOfficialRemoval=true}).Method==UninstallMethod.Unavailable,"Managed application without supported command stays blocked");
            Check(Decide(app with{Location=Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)}).Method==UninstallMethod.Unavailable,"Shared software root cannot be directly removed");
            Check(Decide(app with{Location=Path.Combine(root,"missing")}).Method==UninstallMethod.Unavailable,"Missing installation cannot enable direct deletion");
            var suspected=app with{Name="Synthetic Battle Game",Publisher="Electronic Arts"};
            Check(GameClassificationService.Classify(suspected)!=GameClassificationService.Classification.VerifiedGame,"Publisher and game-name heuristics do not establish launcher ownership");
            Check(Decide(suspected).Method==UninstallMethod.Unavailable,"Ambiguous game cannot fall through into direct deletion");
            var game=app with{Identifier="epic:synthetic-id",Source="Epic Games",RequiresOfficialRemoval=true,Uninstall="\""+executable+"\" --fixture-never-executed"};
            var games=Decide(game);
            Check(games.GamesEnabled&&!games.DeepEnabled,"Verified game selects only Games Uninstall");
            Check(games.GameRoute?.Target==executable&&!games.GameRoute.ManualInteraction,"Registered game command resolves without launcher-library browsing");
            Check(!string.IsNullOrWhiteSpace(games.Tooltip(UninstallMethod.Deep,true))&&!string.IsNullOrWhiteSpace(official.Explanation(false)),"Localized selection explanations are available");
            Check(UninstallCommandService.TryParse("\""+executable+"\" --value \"two words\"",out _,out var args)&&args.SequenceEqual(new[]{"--value","two words"}),"Arguments preserve token boundaries without a shell");
            Check(!UninstallCommandService.TryParse(Path.Combine(Environment.SystemDirectory,"cmd.exe")+" /c echo no",out _,out _),"Shell interpreters are rejected");
            Check(!UninstallCommandService.TryParse("relative.exe /uninstall",out _,out _),"Relative uninstall executables are rejected");
            var unverified=UninstallReportService.FromProbe(app,new(false,false,true,"Unverified folder remains"),null,"fixture");
            Check(unverified.Outcome==UninstallOutcome.VerificationIncomplete,"Unverified remaining folder is not reported as successful removal");
            var complete=UninstallReportService.FromProbe(app,new(true,false,false,"Verified"),null,"fixture");
            Check(complete.Outcome==UninstallOutcome.SuccessfullyRemoved,"Confirmed removal without eligible leftovers reports success");
            var observed=new LeftoverCleanupResult(0,0,0,0,0,0,0,0,0,0,[],[],[],[Path.Combine(root,"synthetic.log")],[]);
            var leftover=UninstallReportService.FromProbe(app,new(true,false,true,"Verified payload removal"),observed,"fixture");
            Check(leftover.Outcome==UninstallOutcome.RemovedWithLeftovers,"Confirmed removal with verified leftovers has distinct outcome");
            string gameDir=Path.Combine(root,"steamapps","common","Synthetic");Directory.CreateDirectory(gameDir);
            File.WriteAllText(Path.Combine(gameDir,"retained.log"),"synthetic leftover; not deleted by this test");
            var steam=app with{Identifier="steam:987654399",Source="Steam",Location=gameDir,RequiresOfficialRemoval=true};
            var snapshot=new UninstallSnapshot(steam,[],[],[],[Path.Combine(gameDir,"game.exe")]);
            var probe=UninstallMonitorService.Probe(steam,snapshot);
            Check(probe.Confirmed&&probe.InstallationFolderPresent&&!probe.MainExecutablePresent,"Absent captured game payload and manifest allow leftover inspection despite retained log");
            File.WriteAllText(Path.Combine(root,"steamapps","appmanifest_987654399.acf"),"synthetic manifest");
            Check(!UninstallMonitorService.Probe(steam,snapshot).Confirmed,"Authoritative launcher record still blocks completed-removal claim");
            Check(File.Exists(Path.Combine(gameDir,"retained.log")),"Capability and verification checks never remove files");
            Console.WriteLine(passed+" focused non-destructive Smart Uninstall checks passed.");return 0;
        }
        catch(Exception e){Console.Error.WriteLine("FAIL "+e);return 1;}
        finally{Directory.Delete(root,true);}
    }
}
