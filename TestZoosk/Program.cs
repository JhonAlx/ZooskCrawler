using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using CommandLine;
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
    internal static class Program
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static Dictionary<string, string> CsvOptions { get; set; }
        private static Dictionary<string, string> ProfilesData { get; set; }
        private static Dictionary<string, string> ContactedProfiles { get; set; }
        private static int WaitMaxLimit { get; set; }

        private static void Main(string[] args)
        {
            var options = new Arguments();

            if (Parser.Default.ParseArguments(args, options))
            {
                SetLoggingLevel(options.Debug ? Level.Debug : Level.Info);

                if (options.Log)
                {
                    if (!string.IsNullOrEmpty(options.LogFile))
                    {
                        ChangeFileAppenderFileName(options.LogFile);
                    }
                }
                else
                {
                    DeactivateFileAppender();
                }

                if (string.IsNullOrEmpty(options.Contacts))
                    options.Contacts = "Contacts.csv";

                Log.Info("=======================================");
                Log.Debug("Options loaded > ");
                Log.Debug($"Username: {options.Username}");
                Log.Debug($"Password: {options.Password}");
                Log.Debug($"Debug: {options.Debug}");
                Log.Debug($"Log: {options.Log}");
                Log.Debug($"LogFile: {options.LogFile}");
                Log.Debug($"OptionsFile: {options.OptionsFile}");
                Log.Debug($"Contacts: {options.Contacts}");
                Log.Debug($"GuiOrCli: {options.GuiOrCli}");
                Log.Debug($"TotalVsContactedVsNew: {options.TotalVsContactedVsNew}");

                Log.Info("Parsing options file");
                ParseOptionsFile(options.OptionsFile);

                foreach (var option in CsvOptions)
                    Log.Debug($"{option.Key} - {option.Value}");

                if (CsvOptions.ContainsKey("Max_wait"))
                    WaitMaxLimit = Convert.ToInt32(CsvOptions["Max_wait"]);

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
                driver.Close();
            }

            driver.Close();
            Log.Info("Process finished");

            if (arguments.TotalVsContactedVsNew)
            {
                Log.Info($"TOTAL  {ProfilesData.Count}");
                Log.Info($"CONTACTED {ContactedProfiles.Count}");
                Log.Info($"NEW {ProfilesData.Count - ContactedProfiles.Count}");
            }
        }

        private static void ProcessProfiles(IWebDriver driver)
        {
            var profilesCounter = 1;

            foreach (var profile in ProfilesData)
            {
                Log.Debug($"Profile counter: {profilesCounter}");

                if (profilesCounter <= Convert.ToInt32(CsvOptions["Number_of_matches_to_email"]))
                {
                    if (!ContactedProfiles.ContainsKey(profile.Key))
                    {
                        RandomWait();
                        Log.Info("Switching profile");
                        Log.Debug($"{profile.Key} - {profile.Value}");
                        driver.Url = $"https://www.zoosk.com/personals/datecard/{profile.Key}/about";
                        driver.Navigate();

                        //Send message here
                        RandomWait();
                        Log.Info("Message should be sent here");

                        Log.Info("Adding profile to contacted list");
                        ContactedProfiles.Add(profile.Key, profile.Value);

                        Log.Info("Sleeping for a moment");
                        RandomWait();

                        profilesCounter++;
                    }
                    else
                    {
                        Log.Info("Profile already contacted, skipping");
                    }
                }
                else
                {
                    Log.Info("Number of matches to email reached, breaking profiles loop");
                    break;
                }
            }
        }

        private static void GetProfiles(IWebDriver driver, WebDriverWait wait)
        {
            Log.Info("Getting profiles data");

            while (true)
            {
                RandomWait();
                wait.Until(ExpectedConditions.ElementExists(By.XPath("//*[@data-zat='profile-pagination-next']")));

                var nextButton = driver.FindElement(By.XPath("//*[@data-zat='profile-pagination-next']"));

                //Gather profiles and send messages if not contacted here 
                var profiles = driver.FindElements(By.XPath("//li[@class='grid-tile']"));

                foreach (var profile in profiles)
                {
                    var profileDataGuid = profile.GetAttribute("data-guid");
                    var profileName = profile.FindElement(By.TagName("h4")).Text;

                    Log.Info($"Adding profile {profileDataGuid} to message queue");
                    Log.Debug($"{profileDataGuid} - {profileName}");
                    ProfilesData.Add(profileDataGuid, profileName);
                }

                if (nextButton.GetAttribute("aria-disabled") == "true")
                {
                    Log.Info("No more profiles found on search, breaking loop");
                    break;
                }

                Log.Info("Processing pagination");
                nextButton.Click();
                RandomWait();
            }
        }

        private static void SearchProfiles(IWebDriver driver, WebDriverWait wait)
        {
            RandomWait();
            Log.Info("Waiting for the search link to show up");
            var searchLink =
                wait.Until(ExpectedConditions.ElementExists(By.XPath("//span[@data-zat='profile-edit-search-link']")));
            Log.Info("Link loaded, clicking it");
            searchLink.Click();

            RandomWait();
            Log.Info("Switching to saved searches tab");
            wait.Until(ExpectedConditions.ElementExists(By.XPath("//ul[@role='tablist']/li[2]"))).Click();

            RandomWait();
            Log.Info("Performing a mouse over over selected saved search");
            Log.Debug($"Saved search {CsvOptions["Saved_Search"]}");
            var act = new Actions(driver);
            act.MoveToElement(
                    driver.FindElement(
                        By.XPath($"//section[descendant::h2[contains(text(), '{CsvOptions["Saved_Search"]}')]]")))
                .Perform();

            RandomWait();
            Log.Info("Clicking search button");
            driver.FindElements(
                    By.XPath(
                        $"//section[descendant::h2[contains(text(), '{CsvOptions["Saved_Search"]}')]]/footer/span/span"))
                [0]
                .Click();

            RandomWait();
            Log.Info("Waiting for grid button to show up");
            Thread.Sleep(5000);
            var gridButton =
                wait.Until(ExpectedConditions.ElementExists(By.XPath("//span[@class='view-toggle view-toggle-grid']")));

            RandomWait();
            Log.Info("Grid button loaded, clicking it");
            gridButton.Click();

            RandomWait();
        }

        private static void Login(IWebDriver driver, WebDriverWait wait, Arguments arguments)
        {
            Log.Info("Opening browser");
            driver.Navigate();

            RandomWait();
            Log.Info("Opening login form");
            driver.FindElement(By.XPath("//span[@id='login-form-trigger']")).Click();

            Log.Info("Waiting for login form to show up");
            wait.Until(ExpectedConditions.ElementExists(By.XPath("//div[@class='modal-wrapper-absolute']")));

            RandomWait();
            Log.Info("Typing credentials");
            Log.Debug($"User: {arguments.Username} - Password: {arguments.Password}");
            driver.FindElements(By.XPath("//input[@name='email']"))[1].SendKeys(arguments.Username);
            RandomWait();
            driver.FindElements(By.XPath("//input[@name='password']"))[1].SendKeys(arguments.Password);
            RandomWait();
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
                var driver = new ChromeDriver {Url = "https://www.zoosk.com/"};
                //driver.Manage().Window.Maximize();

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

            currentLogger.Hierarchy.Threshold = level;
        }

        private static void ChangeFileAppenderFileName(string optionsLogFile)
        {
            var currentLogger = (Logger)Log.Logger;

            foreach (var appender in currentLogger.Hierarchy.Root.Appenders)
            {
                // ReSharper disable once CanBeReplacedWithTryCastAndCheckForNull
                if (appender is RollingFileAppender)
                {
                    RollingFileAppender rfa = (RollingFileAppender)appender;
                    rfa.File = optionsLogFile;
                    rfa.ActivateOptions();
                    break;
                }
            }
        }

        private static void DeactivateFileAppender()
        {
            var currentLogger = (Logger)Log.Logger;

            foreach (var appender in currentLogger.Hierarchy.Root.Appenders)
            {
                if (appender is RollingFileAppender)
                {
                    AppenderSkeleton apSkel = appender as AppenderSkeleton;

                    apSkel.Close();
                    break;
                }
            }
        }

        private static void RandomWait()
        {
            var wait = new Random().Next(1, WaitMaxLimit + 1);
            Log.Debug($"Waiting for {wait} seconds");
            Thread.Sleep(TimeSpan.FromSeconds(wait));
        }
    }
}