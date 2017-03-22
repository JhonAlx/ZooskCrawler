using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using CommandLine;
using CommandLine.Text;
using log4net;
using log4net.Appender;
using log4net.Config;
using log4net.Core;
using log4net.Repository.Hierarchy;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Interactions;
using OpenQA.Selenium.PhantomJS;
using OpenQA.Selenium.Support.UI;

[assembly: XmlConfigurator(Watch = true)]

namespace ZooskCrawler
{
    internal class Program
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static Dictionary<string, string> CsvOptions { get; set; }
        private static Dictionary<string, string> ProfilesData { get; set; }
        private static Dictionary<string, string> ContactedProfiles { get; set; }

        private static void Main(string[] args)
        {
            var options = new Arguments();

            if (Parser.Default.ParseArguments(args, options))
            {
                SetLoggingLevel(options.Debug ? Level.Debug : options.Log ? Level.Info : Level.Off);

                Log.Debug("Options loaded > ");
                Log.Debug(options.Username);
                Log.Debug(options.Password);
                Log.Debug(options.Debug);
                Log.Debug(options.Log);
                Log.Debug(options.OptionsFile);
                Log.Debug(options.Contacts);
                Log.Debug(options.GuiOrCli);

                Log.Info("Parsing options file");
                ParseOptionsFile(options.OptionsFile);

                foreach (var option in CsvOptions)
                    Log.Debug($"{option.Key} - {option.Value}");

                Log.Info("Parsing contacted file");
                LoadContactedProfiles(options.Contacts);

                Log.Info("Finished parsing options file");

                Log.Info("Starting process");
                StartProcess(options);
            }
            else
            {
                options.GetUsage();
            }
        }

        private static void LoadContactedProfiles(string contactsFile)
        {
            if (File.Exists(contactsFile))
                using (var sr = new StreamReader(contactsFile))
                {
                    string line;
                    ContactedProfiles = new Dictionary<string, string>();

                    while ((line = sr.ReadLine()) != null)
                    {
                        var values = line.Split(',');

                        ContactedProfiles.Add(values[0].Trim(), values[1].Trim());
                    }
                }
            else
                ContactedProfiles = new Dictionary<string, string>();
        }

        private static void ParseOptionsFile(string optionsFile)
        {
            using (var sr = new StreamReader(optionsFile))
            {
                string line;
                CsvOptions = new Dictionary<string, string>();

                while ((line = sr.ReadLine()) != null)
                {
                    var values = line.Split(',');

                    CsvOptions.Add(values[0].Trim(), values[1].Trim());
                }
            }
        }

        private static void StartProcess(Arguments arguments)
        {
            var driver = GetDriver(arguments.GuiOrCli);
            var wait = new WebDriverWait(driver, TimeSpan.FromMinutes(5));
            ProfilesData = new Dictionary<string, string>();

            try
            {
                Login(driver, wait, arguments);
                SearchProfiles(driver, wait);
                GetProfiles(driver, wait);
                ProcessProfiles(driver);
                SaveContactedProfiles(ContactedProfiles, arguments.Contacts);
            }
            catch (Exception e)
            {
                Log.Error(e);
            }

            driver.Close();
            Log.Info("Process finished");
        }

        private static void ProcessProfiles(IWebDriver driver)
        {
            foreach (var profile in ProfilesData)
            {
                Log.Info("Switching profile");
                Log.Debug($"{profile.Key} - {profile.Value}");
                driver.Url = $"https://www.zoosk.com/personals/datecard/{profile.Key}/about";
                driver.Navigate();

                //Send message here
                Log.Info("Message should be sent here");

                Log.Info("Addiing profile to contacted list");
                ContactedProfiles.Add(profile.Key, profile.Value);

                Log.Info("Sleeping for a moment");
                Thread.Sleep(10000);
            }
        }

        private static void GetProfiles(IWebDriver driver, WebDriverWait wait)
        {
            Log.Info("Getting profiles data");
            while (true)
            {
                wait.Until(ExpectedConditions.ElementExists(By.XPath("//*[@data-zat='profile-pagination-next']")));

                var nextButton = driver.FindElement(By.XPath("//*[@data-zat='profile-pagination-next']"));

                //Gather profiles and send messages if not contacted here 
                var profiles = driver.FindElements(By.XPath("//li[@class='grid-tile']"));

                foreach (var profile in profiles)
                {
                    var profileDataGuid = profile.GetAttribute("data-guid");
                    var profileName = profile.FindElement(By.TagName("h4")).Text;

                    if (!ContactedProfiles.ContainsKey(profileDataGuid))
                    {
                        Log.Info($"Adding profile {profileDataGuid} to message queue");
                        Log.Debug($"{profileDataGuid} - {profileName}");
                        ProfilesData.Add(profileDataGuid, profileName);
                    }
                    else
                    {
                        Log.Info($"Profile {profileDataGuid} already contacted, skipping");
                    }
                }

                if (nextButton.GetAttribute("aria-disabled") == "true")
                {
                    Log.Info("No more profiles found on search, breaking loop");
                    break;
                }

                Log.Info("Processing pagination");
                nextButton.Click();
                Thread.Sleep(5000);
            }
        }

        private static void SearchProfiles(IWebDriver driver, WebDriverWait wait)
        {
            Log.Info("Waiting for the search link to show up");
            var searchLink =
                wait.Until(ExpectedConditions.ElementExists(By.XPath("//span[@data-zat='profile-edit-search-link']")));
            Log.Info("Link loaded, clicking it");
            searchLink.Click();

            Log.Info("Switching to saved searches tab");
            Thread.Sleep(5000);
            wait.Until(ExpectedConditions.ElementExists(By.XPath("//ul[@role='tablist']/li[2]"))).Click();

            Log.Info("Performing a mouse over over selected saved search");
            Log.Debug($"Saved search {CsvOptions["Saved_Search"]}");
            var act = new Actions(driver);
            act.MoveToElement(
                    driver.FindElement(
                        By.XPath($"//section[descendant::h2[contains(text(), '{CsvOptions["Saved_Search"]}')]]")))
                .Perform();

            Thread.Sleep(1000);
            Log.Info("Clicking search button");
            driver.FindElements(By.XPath("//section[descendant::h2[contains(text(), 'test')]]/footer/span/span"))[0]
                .Click();

            Log.Info("Waiting for grid button to show up");
            Thread.Sleep(5000);
            var gridButton =
                wait.Until(ExpectedConditions.ElementExists(By.XPath("//span[@class='view-toggle view-toggle-grid']")));

            Log.Info("Grid button loaded, clicking it");
            gridButton.Click();

            Thread.Sleep(5000);
        }

        private static void Login(IWebDriver driver, WebDriverWait wait, Arguments arguments)
        {
            Log.Info("Opening browser");
            driver.Navigate();

            Log.Info("Opening login form");
            driver.FindElement(By.XPath("//span[@id='login-form-trigger']")).Click();

            Log.Info("Waiting for login form to show up");
            wait.Until(ExpectedConditions.ElementExists(By.XPath("//div[@class='modal-wrapper-absolute']")));

            Log.Info("Typing credentials");
            Log.Debug($"User: {arguments.Username} - Password: {arguments.Password}");
            driver.FindElements(By.XPath("//input[@name='email']"))[1].SendKeys(arguments.Username);
            driver.FindElements(By.XPath("//input[@name='password']"))[1].SendKeys(arguments.Password);
            driver.FindElement(By.XPath("//button[@ng-click='loginPopoverCtrl.onLogInClick()']")).Click();
        }

        private static void SaveContactedProfiles(Dictionary<string, string> contactedProfiles, string contactsFile)
        {
            using (var sw = new StreamWriter(contactsFile, false))
            {
                foreach (var profile in contactedProfiles)
                    sw.WriteLine($"{profile.Key},{profile.Value}");
            }
        }

        private static IWebDriver GetDriver(bool guiOrCli)
        {
            if (guiOrCli)
            {
                var options = new ChromeOptions();
                options.AddArgument("start-maximized");

                var driver = new ChromeDriver(options) {Url = "https://www.zoosk.com/"};

                return driver;
            }
            else
            {
                var driver = new PhantomJSDriver {Url = "https://www.zoosk.com/"};
                driver.Manage().Window.Maximize();

                return driver;
            }
        }

        /// <summary>
        ///     Sets the logging level.
        /// </summary>
        /// <param name="level">Type of the log.</param>
        private static void SetLoggingLevel(Level level)
        {
            var currentLogger = (Logger) Log.Logger;

            foreach (var appender in currentLogger.Appenders)
            {
                var apSkel = appender as AppenderSkeleton;

                if (apSkel != null) apSkel.Threshold = level;
            }
        }
    }

    internal class Arguments
    {
        [Option('u', "user", Required = true, HelpText = "Zoosk username")]
        public string Username { get; set; }

        [Option('p', "pass", Required = true, HelpText = "Zoosk password")]
        public string Password { get; set; }

        [Option('o', "options", Required = true, HelpText = "Options data file path")]
        public string OptionsFile { get; set; }

        [Option('c', "contacts", Required = true, HelpText = "Contacted data file path")]
        public string Contacts { get; set; }

        [Option('l', "log", Required = false, HelpText = "Log program activities")]
        public bool Log { get; set; }

        [Option('d', "debug", Required = false, HelpText = "Generate verbose logging output for debugging")]
        public bool Debug { get; set; }

        [Option('g', "graphic", Required = false,
            HelpText =
                "If this flag is set, the crawler will work with a Chrome window, otherwise it will work with a PhantomJS process"
        )]
        public bool GuiOrCli { get; set; }

        //[Option('t', "totalNewVsContacted", Required = false,
        //    HelpText =
        //        "Return the number of users that meet the search criteria (TOTAL), the number of those users that have already been contacted(CONTACTED), and the number that have not yet been contacted(NEW)"
        //)]
        //public string TotalNewVsContacted { get; set; }

        [HelpOption('h', "help")]
        public string GetUsage()
        {
            return HelpText.AutoBuild(this);
        }
    }
}