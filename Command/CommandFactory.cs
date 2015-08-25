﻿using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using NLog;
using System.Collections.Generic;
using System.Text;
using System.Globalization;
using System.Text.RegularExpressions;

namespace sensu_client.Command
{

    public static class CommandProviders
    {
        public static string  PowerShell = "powershell";
        public static string Ruby = "ruby";
        public static string Cmd = "cmd";

    }
   
    public struct CommandResult 
    {
        public string Output { get; set; }
        public int Status { get; set; }
        public string Duration { get; set; }
    }

    public static class CommandFactory
    {

        public static Command Create(CommandConfiguration commandConfiguration, string command)
        {
            var command_lower = command.ToLower();
            if (command_lower.StartsWith(PerformanceCounterCommand.PREFIX)) return new PerformanceCounterCommand(commandConfiguration, command);
            if (command_lower.Contains(".ps1")) return new PowerShellCommand(commandConfiguration, command);
            if (command_lower.Contains(".rb")) return new RubyCommand(commandConfiguration, command);

            return new ShellCommand(commandConfiguration, command);
        }
    }

    public abstract class Command
    {
        protected static readonly Logger Log = LogManager.GetCurrentClassLogger();
        protected readonly CommandConfiguration _commandConfiguration;
        protected readonly string _unparsedCommand;
        private string _arguments;

        protected Command(CommandConfiguration commandConfiguration, string unparsedCommand)
        {
            _commandConfiguration = commandConfiguration;
            _unparsedCommand = unparsedCommand;
        }

        public abstract string FileName { get; protected internal set; }

        public virtual string Arguments
        {
            get
            {
                if (!String.IsNullOrEmpty(_arguments)) return _arguments;

                _arguments = ParseArguments();
                return _arguments;
            }
            protected internal set { _arguments = value; }
        }

        protected abstract string ParseArguments();

        public virtual CommandResult Execute()
        {
            var result = new CommandResult();
            var processstartinfo = new ProcessStartInfo()
                {
                    FileName = FileName,
                    Arguments = Arguments,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = _commandConfiguration.Plugins,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true
                };
            var process = new Process {StartInfo = processstartinfo};
            var stopwatch = new Stopwatch();
            try
            {
                stopwatch.Start();
                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                var errors = process.StandardError.ReadToEnd();
                var status = process.ExitCode;
                if (_commandConfiguration.TimeOut.HasValue)
                {
                    if (!process.WaitForExit(1000*_commandConfiguration.TimeOut.Value))
                    {
                        process.Kill();
                    }
                }
                else
                {
                    process.WaitForExit();
                    process.Close();
                }

                result.Output = String.Format("{0}{1}", output,errors);
                result.Status = status;
                if (!string.IsNullOrEmpty(errors)) Log.Error("Error when executing command: {0} \n resulted in: {1} \n", Arguments,errors);
            }
            catch (Win32Exception ex)
            {
                result.Output = String.Format("Unexpected error: {0}", ex.Message);
                result.Status = 2;
            }
            stopwatch.Stop();
            result.Duration = String.Format("{0:f3}", ((float) stopwatch.ElapsedMilliseconds)/1000);
            return result;

        }
    }

    public class PowerShellCommand : Command
        {
            private string _fileName;
            private string _arguments;
            const string PowershellOptions = "-NoProfile -NonInteractive -NoLogo -ExecutionPolicy Bypass ";
            
            public PowerShellCommand(CommandConfiguration commandConfiguration, string unparsedCommand): base(commandConfiguration,unparsedCommand)
            {
            }

            public override string FileName
            {
                get
                {
                    if (!String.IsNullOrEmpty(_fileName)) return _fileName;

                    _fileName = GetPowerShellExePath();
                    return _fileName;
                }
                protected internal set { _fileName = value; }
            }

        private static string GetPowerShellExePath()
        {
            var systemRoot = Environment.ExpandEnvironmentVariables("%systemroot%").ToLower();
            if (File.Exists(string.Format("{0}\\sysnative\\WindowsPowershell\\v1.0\\powershell.exe", systemRoot)))
            {
                return string.Format("{0}\\sysnative\\WindowsPowershell\\v1.0\\powershell.exe", systemRoot);
            }
            if (File.Exists(string.Format("{0}\\system32\\WindowsPowershell\\v1.0\\powershell.exe", systemRoot)))
            {
                return string.Format("{0}\\system32\\WindowsPowershell\\v1.0\\powershell.exe", systemRoot);
            }
            return "powershell.exe";
        }

        public override string Arguments
            {
                get { 
                    if (!String.IsNullOrEmpty(_arguments)) return _arguments;

                        _arguments = ParseArguments();
                return _arguments;
                }
                protected internal set { _arguments = value; }
            }

         protected override string ParseArguments()
        {
            int lastSlash = _unparsedCommand.LastIndexOf('/');
            var powershellargument = (lastSlash > -1) ? _unparsedCommand.Substring(lastSlash + 1) : _unparsedCommand;
            return String.Format("{0} -FILE {1}\\{2}", PowershellOptions, _commandConfiguration.Plugins, powershellargument);
        }
     }

        public class RubyCommand : Command
        {
            //string envRubyPath = Environment.GetEnvironmentVariable("RUBYPATH");
            private string _fileName;
            private string _arguments;

            public RubyCommand(CommandConfiguration commandConfiguration, string unparsedCommand)
                : base(commandConfiguration, unparsedCommand)
            {
            }

   
            public override string FileName
            {
                get
                {
                    if (!String.IsNullOrEmpty(_fileName)) return _fileName;

                    _fileName = RubyExePath();
                    return _fileName;
                }
                protected internal set { _fileName = value; }
            }

            private static string RubyExePath()
            {
                var defaultSensuClientPath = @"c:\opt\sensu\embedded\bin";
                var rubyPath = Path.Combine(defaultSensuClientPath, "ruby.exe");
                if (File.Exists(rubyPath))
                {
                    return rubyPath;
                }

                return "ruby.exe";
            }

            public override string Arguments
            {
                get { 
                    if (!String.IsNullOrEmpty(_arguments)) return _arguments;

                        _arguments = ParseArguments();
                         return _arguments;
                }
                protected internal set { _arguments = value; }
            }


            protected override string ParseArguments()
            {
                int lastSlash = _unparsedCommand.LastIndexOf('/');
                var rubyArgument = (lastSlash > -1) ? _unparsedCommand.Substring(lastSlash + 1) : _unparsedCommand;
                return String.Format("{0}\\{1}", _commandConfiguration.Plugins, rubyArgument);
            }
        }
        public class ShellCommand : Command
        {
            private string _fileName = String.Format("{0}\\cmd.exe",Environment.SystemDirectory);
           
            public ShellCommand(CommandConfiguration commandConfiguration, string unparsedCommand): base(commandConfiguration, unparsedCommand)
            {
            }
     
            public override string FileName
            {
                get { return _fileName; }
                protected internal set { _fileName = value; }
            }

            protected override string ParseArguments()
            {
                return String.Format("'{0}'", _unparsedCommand);
            }
        }

    public class PerformanceCounterCommand : Command
    {
        public static string PREFIX = "!perfcounter> ";
        private static Dictionary<string, List<PerformanceCounter>> counters = new Dictionary<string, List<PerformanceCounter>>();
        private static PerformanceCounterRegEx DefaultPerfCounterRegEx = new PerformanceCounterRegEx();

        public PerformanceCounterCommand(CommandConfiguration commandConfiguration, string unparsedCommand) : base(commandConfiguration, unparsedCommand)
        {
        }

        public override string FileName
        {
            // I'm afraid this method is not required in this Command
            get
            {
                return "";
            }

            protected internal set {}
        }

        protected override string ParseArguments()
        {
            // retire the magic word
            return _unparsedCommand.Substring(PREFIX.Length);
        }

        public override CommandResult Execute()
        {
            var result = new CommandResult();
            var stopwatch = new Stopwatch();
            result.Status = 0;
            stopwatch.Start();
            {
                string[] splittedArguments = ParseArguments().Split(';');
                var counterlist = getCounterlist(splittedArguments[0]);
                var parameters = ParseParameters(splittedArguments);

                var unixTimestamp = (Int32)(DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1))).TotalSeconds;
                var stdout = new StringBuilder();
                var stderr = new StringBuilder();
                string schema;
                
                foreach (var counter in counterlist)
                {
                    if (parameters.ContainsKey("schema"))
                        schema = parameters["schema"];
                    else
                        schema = String.Format(CultureInfo.InvariantCulture, "{0}.{1}", System.Environment.MachineName, "performance_counter");

                    try
                    {
                        var value = counter.NextValue();
                        stdout.AppendLine(
                            String.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2} {3:f2} {4}",
                                schema,
                                normalizeString(counter.CategoryName),
                                normalizeString(counter.CounterName.Replace('.', '_').Replace("%", "percent_")).Replace("percent_", "percent."),
                                value,
                                unixTimestamp
                            )
                        );
                        if (result.Status == 0) {
                            if (parameters.ContainsKey("warn") && value > Int32.Parse(parameters["warn"])) {
                                result.Status = 1;
                                stderr.AppendLine(String.Format("# WARNING: {0} has value {1} > {2}", counter.ToString(), value, parameters["warn"]));
                            }
                            else if (parameters.ContainsKey("error") && value > Int32.Parse(parameters["error"]))
                            {
                                result.Status = 1;
                                stderr.AppendLine(String.Format("# CRITICAL: {0} has value {1} > {2}", counter.ToString(), value, parameters["error"]));
                            }
                        }
                    } catch (Exception e)
                    {
                        Log.Warn("Error running performance counter " + counter.CounterName, e);
                        stderr.AppendLine("# " + e.Message);
                        result.Status = 2;
                    }
                    result.Output = stderr.Append(stdout).ToString().Trim(' ', '_');
                }
            }
            stopwatch.Stop();
            result.Duration = String.Format("{0:f3}", ((float)stopwatch.ElapsedMilliseconds) / 1000);

            return result;
        }
        private string normalizeString(string str)
        {
            return Regex.Replace(str, @"[^A-Za-z0-9]+", "_");
        }

        private List<PerformanceCounter> getCounterlist(string counterName)
        {
            if (!counters.ContainsKey(counterName)) {
                List<System.Diagnostics.PerformanceCounter> counterlist = new List<PerformanceCounter>();
                var counterData = DefaultPerfCounterRegEx.split(counterName);
                try {
                    PerformanceCounterCategory mycat = new PerformanceCounterCategory(counterData.Category);
                    foreach (var counter in mycat.GetCounters(counterData.Instance))
                    {
                        if ( !counter.CounterName.Equals(counterData.Counter, StringComparison.InvariantCultureIgnoreCase))
                            continue;
                        counterlist.Add(counter);
                        counter.NextValue(); // Initialize performance counters in order to avoid them to return 0.
                    }
                    counters.Add(counterName, counterlist);
                }
                catch (Exception e)
                {
                    Log.Error(String.Format("Counter {0} will be ignored due to errors", counterName));
                    Log.Error(e);
                }

            }
            return counters[counterName];
        }

        private Dictionary<string, string> ParseParameters(string[] split)
        {
            Dictionary<string, string> parameters = new Dictionary<string, string>();

            for (var i = 1; i < split.Length; ++i)
            {
                var Item = split[i];
                if (!Item.Contains("="))
                {
                    Log.Warn("Invalid format for argument {0}. Ignored.", Item);
                    continue;
                }
                string[] aux = Item.Split(new char[] { '=' }, 2);
                string key = aux[0].Trim();
                string value = aux[1].Trim();

                parameters[key] = value;
            }
            return parameters;
        }
    }

    public class PerformanceCounterRegEx
    {
        Regex regex = new Regex(
            @"^\\?" +
            @"(?<category>[^\\\(]+)" +
            @"(?:\(" + @"(?<instance>[^\)]+)" + @"\)\s*)?" +
            @"\\" + 
            @"(?<counter>.*)" +
            @"$"
        );
        public PerformanceCounterData split(string pattern)
        {
            var result = new PerformanceCounterData();
            var match = regex.Match(pattern);

            result.Category = match.Groups["category"].Value.Trim();
            result.Counter = match.Groups["counter"].Value.Trim();
            if (match.Groups["instance"].Success)
            {
                result.Instance = match.Groups["instance"].Value.Trim();
            }
            return result;
        }
    }

    public class PerformanceCounterData
    {
        public string Category { get; set; }
        public string Counter { get; set; }
        public string Instance { get; set; }
    }
}