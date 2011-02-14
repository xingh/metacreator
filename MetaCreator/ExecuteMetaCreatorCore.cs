﻿using System;
using System.Diagnostics;
using System.Linq;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

using MetaCreator.AppDomainIsolation;
using MetaCreator.Evaluation;
using MetaCreator.Utils;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace MetaCreator
{
	internal class ExecuteMetaCreatorCore
	{
		internal ExecuteMetaCreatorCore()
		{

		}

		public ExecuteMetaCreatorCore(ITaskItem[] sources, ITaskItem[] references, string intermediateOutputPath, string projDir, IBuildErrorLogger buildErrorLogger)
		{
			Sources = sources;
			References = references;
			IntermediateOutputPathRelative = intermediateOutputPath;
			ProjDir = projDir;
			BuildErrorLogger = buildErrorLogger;
		}

		#region INPUTS

		public ITaskItem[] Sources { get; set; }
		public ITaskItem[] References { get; set; }
		public string IntermediateOutputPathFull { get; set; }
		public string IntermediateOutputPathRelative { get; set; }
		public string ProjDir { get; set; }
		public IBuildErrorLogger BuildErrorLogger { get; set; }

		#endregion

		#region OUTPUTS

		readonly List<ITaskItem> _addFiles = new List<ITaskItem>();

		public IEnumerable<ITaskItem> AddFiles
		{
			get { return _addFiles.AsReadOnly(); }
		}

		readonly List<ITaskItem> _removeFiles = new List<ITaskItem>();

		public IEnumerable<ITaskItem> RemoveFiles
		{
			get { return _removeFiles.AsReadOnly(); }
		}

		#endregion

		#region Fields and Consts

		internal static readonly Regex _rxStringInterpolVerbatim = new Regex(@"@""([^""]+)""");
		internal static readonly Regex _rxStringInterpolInside = new Regex(@"{([^\d].*?)}");
		internal static readonly Regex _rxStringInterpolNoVerbatim = new Regex(@"(?<!@)""(.*?[^\\])""");

		#endregion

		public void Initialize()
		{
			if (string.IsNullOrEmpty(ProjDir))
			{
				throw new Exception("ProjDir not defined");
			}

			if (string.IsNullOrEmpty(IntermediateOutputPathRelative))
			{
				throw new Exception("IntermediateOutputPathRelative not defined");
			}

			if (Sources.OrEmpty().Count() <= 0)
			{
				throw new Exception("Sources not specified");
			}

			if (ProjDir != Path.GetFullPath(ProjDir))
			{
				throw new Exception("ProjDir is not full path");
			}

			IntermediateOutputPathFull = Path.Combine(ProjDir, IntermediateOutputPathRelative);

			if (Sources == null)
			{
				Sources = new ITaskItem[0];
			}

			if (References == null)
			{
				References = new ITaskItem[0];
			}
		}

		internal void ProcessFile(ProcessFileCtx ctx)
		{
			ctx.FileProcessedContent = ctx.FileOriginalContent;

			ctx.FileProcessedContent = EvaluateMetacode(ctx.FileProcessedContent, ctx);

			if (ctx.EnabledStringInterpolation)
			{
				ProcessStringInterpolation(ctx);
			}

		}

		static void ProcessStringInterpolation(ProcessFileCtx ctx)
		{
			ctx.FileProcessedContent = _rxStringInterpolNoVerbatim.Replace(ctx.FileProcessedContent, match =>
			{
				var stringValue = match.Groups[1].Value;
				stringValue = _rxStringInterpolInside.Replace(stringValue, m =>
				{
					ctx.MarkMacrosAndSaveCaptureState(match);
					var val = m.Groups[1].Value;
					//if(string.IsNullOrEmpty(val))
					//{
					//   return null;
					//}
					return "\"+" + val + "+\"";
				});
				stringValue = "\"" + stringValue + "\"";
				// trim "" + and + ""
				if (stringValue.StartsWith("\"\"+"))
				{
					stringValue = stringValue.Substring(3);
				}
				if (stringValue.EndsWith("+\"\""))
				{
					stringValue = stringValue.Substring(0, stringValue.Length - 3);
				}
				return stringValue;
			});

			ctx.FileProcessedContent = _rxStringInterpolVerbatim.Replace(ctx.FileProcessedContent, match =>
			{
				var stringValue = match.Groups[1].Value;
				stringValue = _rxStringInterpolInside.Replace(stringValue, m =>
				{
					ctx.MarkMacrosAndSaveCaptureState(match);
					var val = m.Groups[1].Value;
					//if (string.IsNullOrEmpty(val))
					//{
					//   return null;
					//}
					return "\"+" + val + "+@\"";
				});
				stringValue = "@\"" + stringValue + "\"";
				return stringValue;
			});
		}

		private string EvaluateMetacode(string code, ProcessFileCtx ctx)
		{
			var codeBuilder = new Code1Builder();
			var metacode = codeBuilder.Build(code, ctx);
			if (ctx.NumberOfMacrosProcessed == 0)
			{
				return code;
			}
			var evaluationResult = ctx.AppDomFactory.AnotherAppDomMarshal.Evaluate(new AnotherAppDomInputData
			{
				Metacode = metacode,
				References = ctx.References,
			});
			ctx.AppDomFactory.MarkDirectoryPathToRemoveAfterUnloadDomain(evaluationResult.CompileTempPath);
			var codeAnalyzer = new Code4Analyze();
			codeAnalyzer.Analyze(evaluationResult, ctx);
			return evaluationResult.ResultBody;
		}

		public bool Execute()
		{
			// Debugger.Launch();
			Initialize();

			using (var appDomFactory = AnotherAppDomFactory.AppDomainLiveScope())
			{
				int totalMacrosProcessed = 0;
				var totalTime = Stopwatch.StartNew();
				foreach (var sourceFile in Sources)
				{
					var fileName = sourceFile.ItemSpec;
					var isExternalLink = fileName.StartsWith("..");
					var ext = Path.GetExtension(fileName);
					string replacementFile;
					if (isExternalLink)
					{
						replacementFile = Path.Combine("_Linked", Path.GetFileNameWithoutExtension(fileName) + ".g" + Path.GetExtension(fileName));
					}
					else
					{
						replacementFile = fileName.Substring(0, fileName.Length - ext.Length) + ".g" + ext;
					}
					var replacementFileRelativePath = Path.Combine(IntermediateOutputPathRelative, replacementFile);
					var replacementFileAbsolutePath = Path.GetFullPath(replacementFileRelativePath);

					var ctx = new ProcessFileCtx()
					{
						AppDomFactory = appDomFactory,
						BuildErrorLogger = BuildErrorLogger,
						OriginalFileName = fileName,
						FileOriginalContent = File.ReadAllText(fileName),
						ReplacementRelativePath = replacementFileRelativePath,
						ReplacementAbsolutePath = replacementFileAbsolutePath,
						IntermediateOutputPathRelative = IntermediateOutputPathRelative,
						IntermediateOutputPathFull = IntermediateOutputPathFull,
						ProjDir = ProjDir,
						ReferencesOriginal = References.Select(x => x.ItemSpec).ToArray(),
					};

					try
					{
						ProcessFile(ctx);
					}
					catch (FailBuildingException)
					{
						// Already logged to msbuild. Just fail the building.
						return false;
					}

					if (ctx.NumberOfMacrosProcessed > 0)
					{
						BuildErrorLogger.LogDebug("fileName = " + fileName);
						BuildErrorLogger.LogDebug("replacementFile = " + replacementFile);
						BuildErrorLogger.LogDebug("IntermediateOutputPathRelative = " + IntermediateOutputPathRelative);
						BuildErrorLogger.LogDebug("replacementFileRelativePath = " + replacementFileRelativePath);
						BuildErrorLogger.LogDebug("replacementFileAbsolutePath = " + replacementFileAbsolutePath);

						totalMacrosProcessed += ctx.NumberOfMacrosProcessed;

						Directory.CreateDirectory(Path.GetDirectoryName(replacementFileAbsolutePath));

						var theSameContent = File.Exists(replacementFileAbsolutePath) &&
													File.ReadAllText(replacementFileAbsolutePath) == ctx.FileProcessedContent;

						if (!theSameContent)
						{
							//if (File.Exists(replacementFileName))
							//{
							//   File.SetAttributes(replacementFileName, File.GetAttributes(replacementFileName) & ~FileAttributes.ReadOnly);
							//}
							File.WriteAllText(replacementFileAbsolutePath, ctx.FileProcessedContent);
							//File.SetAttributes(replacementFileName, File.GetAttributes(replacementFileName) | FileAttributes.ReadOnly);
						}

						BuildErrorLogger.LogOutputMessage(fileName + " - " + ctx.NumberOfMacrosProcessed + " macros processed to => " + replacementFileRelativePath + ". File " + (theSameContent ? "is up to date." : "updated"));

						_removeFiles.Add(sourceFile);
						_addFiles.Add(new TaskItem(replacementFileAbsolutePath));
					}
				}

				if (totalMacrosProcessed == 0)
				{
					BuildErrorLogger.LogOutputMessage("No macros found. Nothing changed. Duration = " + totalTime.ElapsedMilliseconds + "ms");
				}
				else
				{
					BuildErrorLogger.LogOutputMessage("Duration = " + totalTime.ElapsedMilliseconds + "ms");
				}

				return true;
			}
		}

	}
}