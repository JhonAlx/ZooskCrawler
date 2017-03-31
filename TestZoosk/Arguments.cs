using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using CommandLine;
using CommandLine.Text;

namespace ZooskCrawler
{
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
    internal class Arguments
    {
        [Option('u', "user", Required = true, HelpText = "Specify the username", MetaValue = "USERNAME")]
        public string Username { get; set; }

        [Option('p', "pass", Required = true, HelpText = "Specify user password", MetaValue = "PASSWORD")]
        public string Password { get; set; }

        [Option('o', "options", Required = true,
            HelpText =
                "Loads program option from CSV file.",
            MetaValue = "OPTIONFILE")]
        public string OptionsFile { get; set; }

        [Option('c', "contacts", Required = false,
            HelpText =
                "Loads program contacted profiles from CSV file. If no other file name is specified, the default filename used is \'Contacts.csv\'",
            MetaValue = "CONTACTSFILE")]
        public string Contacts { get; set; }

        [Option('l', "log", Required = false,
            HelpText =
                "Logs program activities to file. When -l is not used, program activity is displayed to screen but not saved to log file. If no file name is specified with -f or --logfile, the default filename used is \'Debug.log\'",
            MutuallyExclusiveSet = "log")]
        public bool Log { get; set; }

        [Option('f', "logfile", Required = false,
            HelpText =
                "Log file name", MetaValue = "LOGFILE", MutuallyExclusiveSet = "log")]
        public string LogFile { get; set; }

        [Option('d', "debug", Required = false, HelpText = "Produce verbose output suitable for debugging purposes")]
        public bool Debug { get; set; }

        [Option('g', "graphic", Required = false,
            HelpText =
                "If this flag is set, the crawler will work with a Chrome window, otherwise it will work with a PhantomJS process"
        )]
        public bool GuiOrCli { get; set; }

        [Option('t', "Total_v_Contacted_v_New", Required = false,
            HelpText =
                "Return the number of users that meet the search criteria(TOTAL), the number of those users that have already been contacted(CONTACTED), and the number that have not yet been contacted(NEW)"
        )]
        public bool TotalVsContactedVsNew { get; set; }

        [HelpOption('h', "help", HelpText = "Show this help message and exit")]
        // ReSharper disable once UnusedMethodReturnValue.Global
        public string GetUsage()
        {
            var ht = new HelpText {Heading = "Test"};

            ht.Heading =
                $"Usage: \n{Process.GetCurrentProcess().ProcessName} [--help] [--username USERNAME] [--password PASSWORD] [--options OPTIONFILE] [--contacts CONTACTSFILE] [--LOG LOGFILE] [--DEBUG] [--Total_v_Contacted_v_New]";
            ht.AddDashesToOption = true;
            ht.AddOptions(this);
            ht.AddPostOptionsLine($"Example:");
            ht.AddPostOptionsLine(
                $"{Process.GetCurrentProcess().ProcessName} --username USERNAME --password PASSWORD --options OPTIONFILENAME.csv --Log LOGFILENAME.log --Debug --TCN");

            return ht.ToString();
        }
    }
}