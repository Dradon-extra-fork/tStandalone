﻿using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using static Terraria.ModLoader.Setup.Program;

namespace Terraria.ModLoader.Setup
{
	public class SetupDebugTask : SetupOperation
	{
		private static readonly Version MinMSBuildVersion = new Version(15, 5); //Minimal MSBuild version for the '/restore' flag to work.

		private bool msBuildIsMissing;
		private bool msBuildIsOutdated;
		private bool roslynCompileFailed;
		private bool tMLFNACompileFailed;

		public SetupDebugTask(ITaskInterface taskInterface) : base(taskInterface) { }

		public override void Run() {
			taskInterface.SetStatus("Updating ModCompile version");
			var modCompile = Path.Combine(tMLSteamDir, "ModCompile");
			UpdateModCompileVersion(modCompile);

			taskInterface.SetStatus("Compiling RoslynWrapper");

			msBuildIsMissing = !CheckIfMSBuildIsPresent();
			msBuildIsOutdated = GetMSBuildVersion() < MinMSBuildVersion; 

			//Compile Roslyn.

			roslynCompileFailed = RunCmd("RoslynWrapper", "msbuild",
				"RoslynWrapper.sln /restore /p:Configuration=Release",
				null, null, null, taskInterface.CancellationToken
			) != 0;

			var roslynRefs = new[] {"RoslynWrapper.dll", "Microsoft.CodeAnalysis.dll", "Microsoft.CodeAnalysis.CSharp.dll",
				"System.Collections.Immutable.dll", "System.Reflection.Metadata.dll", "System.IO.FileSystem.dll", "System.IO.FileSystem.Primitives.dll",
				"System.Security.Cryptography.Algorithms.dll", "System.Security.Cryptography.Encoding.dll", "System.Security.Cryptography.Primitives.dll", "System.Security.Cryptography.X509Certificates.dll" };

			foreach (var dll in roslynRefs) {
				string path = Path.Combine("RoslynWrapper/bin/Release/net46", dll);

				if (!roslynCompileFailed || File.Exists(path))
					Copy(path, Path.Combine(modCompile, dll));
			}

			//Compile FNA tML.

			taskInterface.SetStatus("Compiling tModLoader.FNA.exe");
			var references = new[] { "FNA.dll" };
			foreach (var dll in references)
				Copy("references/" + dll, Path.Combine(modCompile, dll));

			tMLFNACompileFailed = RunCmd("solutions", "msbuild",
				"tModLoader.sln /restore /p:Configuration=MacRelease",
				null, null, null, taskInterface.CancellationToken
			) != 0;
		}

		private bool CheckIfMSBuildIsPresent()
			=> RunCmd("RoslynWrapper", "where",
				"msbuild",
				(s) => Console.WriteLine(s), null, null, taskInterface.CancellationToken
			) == 0;

		private Version GetMSBuildVersion() {
			//Try to get its version.

			string msBuildVersionOutput = null;

			RunCmd("RoslynWrapper", "msbuild", "/version",
				str => msBuildVersionOutput = str, null, null, taskInterface.CancellationToken
			);

			int lastLineBreak = msBuildVersionOutput.LastIndexOf('\n');

			if (!Version.TryParse(msBuildVersionOutput.Substring(lastLineBreak+1, msBuildVersionOutput.Length-lastLineBreak-1), out var msBuildVersion))
				throw new Exception($"Couldn't get MSBuild version.");

			return msBuildVersion;
		}

		private void UpdateModCompileVersion(string modCompileDir) {
			var modLoaderCsPath = Path.Combine("src", "tModLoader", "Terraria", "ModLoader", "ModLoader.cs");
			var r = new Regex(@"new Version\((.+?)\).+?string branchName.+?""(.*?)"".+?int beta.+?(\d+?)", RegexOptions.Singleline);
			var match = r.Match(File.ReadAllText(modLoaderCsPath));

			string version = match.Groups[1].Value.Replace(", ", ".");
			string branchName = match.Groups[2].Value;
			int beta = int.Parse(match.Groups[3].Value);

			var versionTag = $"v{version}" +
				(branchName.Length == 0 ? "" : $"-{branchName.ToLower()}") +
				(beta == 0 ? "" : $"-beta{beta}");

			Directory.CreateDirectory(modCompileDir);
			File.WriteAllText(Path.Combine(modCompileDir, "version"), versionTag);
		}

		public override bool Failed() {
			return roslynCompileFailed || tMLFNACompileFailed;
		}

		public override void FinishedDialog() {
			if (msBuildIsMissing)
				throw new Exception("MSBuild could not be found on PATH.");

			if (msBuildIsOutdated)
				throw new Exception($"MSBuild {MinMSBuildVersion} or newer is required. Ensure that 'msbuild' on PATH points to the newest one installed. Or just build RoslynWrapper manually.");

			if (roslynCompileFailed)
				MessageBox.Show("MSBuild Error", "Failed to compile RoslynWrapper.sln.", MessageBoxButtons.OK, MessageBoxIcon.Error);

			if (tMLFNACompileFailed)
				MessageBox.Show(
					"Failed to compile tModLoader.FNA.exe\r\nJust build it from the tModLoader solution.",
					"MSBuild Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
		}
	}
}
