﻿using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Microsoft.Build.Framework;

namespace MetaCreator.Evaluation
{
	/// <summary>
	/// Check for errors
	/// </summary>
	class Code4Analyze
	{
		public void Analyze(EvaluationResult evaluationResult, ProcessFileCtx ctx)
		{
			_ctx = ctx;
			_buildErrorLogger = ctx.BuildErrorLogger;

			ProcessEvaluationResult(evaluationResult);
		}

		void ProcessEvaluationResult(EvaluationResult result)
		{

			// Log meta code compile time warnings
			if (result.Warnings != null)
			{
				foreach (var error in result.Warnings)
				{
					_buildErrorLogger.LogWarningEvent(CreateBuildWarning(result, error));
				}
			}

			var macrosFailed = false;

			// Log meta code compile time errors
			if (result.Errors != null)
			{
				foreach (var error in result.Errors)
				{
					macrosFailed = true;
					_buildErrorLogger.LogErrorEvent(CreateBuildError(result, error));
				}
			}

			// Log meta code run time exceptions
			if (result.EvaluationException != null)
			{
				macrosFailed = true;
				// var linenumber = result.EvaluationException.
				var message = result.EvaluationException.GetType().FullName + ": " + result.EvaluationException.Message;
				_buildErrorLogger.LogOutputMessage(result.EvaluationException.ToString());

				var i = result.EvaluationException.StackTrace.IndexOf('\r');
				if (i <= 0)
				{
					i = result.EvaluationException.StackTrace.Length;
				}
				var stack = result.EvaluationException.StackTrace.Substring(0, i).Trim();

				// at Generator.Run() in c:\Kip\Projects\MetaCreatorRep\UnitTests\ConsoleApplication\Program.cs:line 19

				var match = Regex.Match(stack, @"(?i)at (?'method'[^\s]+) in (?'file'.+):line (?'line'\d+)");
				if (match.Success)
				{
				}
				var lineString = match.Groups["line"].Value;
				int line;
				int.TryParse(lineString, out line);
				_buildErrorLogger.LogErrorEvent(new BuildErrorEventArgs(null, null, match.Groups["file"].Value, line, 0, 0, 0, _metacreatorErrorPrefix + message, null, null));

			}

			// terminate
			if (macrosFailed)
			{
				throw new FailBuildingException("$ terminating, jump to global catch and return false...");
			}
		}

		BuildWarningEventArgs CreateBuildWarning(EvaluationResult result, CompilerError error)
		{
			BuildError_GetLineNumber(error, result); // init non user code
			return new BuildWarningEventArgs(null, null, BuildError_GetFile(result, error), BuildError_GetLineNumber(error, result),
				BuildError_GetColumnNumber(error), 0, 0, BuildError_GetMessage(error, result),
				null, null);
		}

		BuildErrorEventArgs CreateBuildError(EvaluationResult result, CompilerError error)
		{
			return new BuildErrorEventArgs(null, null, BuildError_GetFile(result, error), BuildError_GetLineNumber(error, result),
			                               BuildError_GetColumnNumber(error), 0, 0, BuildError_GetMessage(error, result),
			                               null, null);
		}

		static int BuildError_GetColumnNumber(CompilerError error)
		{
			return error.Column;
		}

		int BuildError_GetLineNumber(CompilerError error, EvaluationResult result)
		{
			return BuildError_GetLineNumberCore(error, result);
//			if (line == _nonUserCodeSpecialLineNumber)
//			{
//				if (_ctx.ErrorRemap)
//				{
//					result.NonUserCode = "...";
//				}
//			}
//			return line;
		}

		static int BuildError_GetLineNumberCore(CompilerError error, EvaluationResult result)
		{
			return error.Line;
//			if (!_ctx.ErrorRemap)
//			{
//				return error.Line;
//			}
//			if (result.NonUserCode != null)
//			{
//				return error.Line;
//			}
//
//			return RemapErrorLineNumber(error.Line, _ctx, result);
		}

		/// <summary>
		/// Allow to receive line number for compile time or run time error
		/// </summary>
		/// <param name="errorLine"></param>
		/// <param name="ctx"></param>
		/// <param name="result"></param>
		/// <returns></returns>
		private int RemapErrorLineNumber(int errorLine, EvaluationResult result)
		{
			return errorLine;
			//var userCodeIndex = result.SourceCode.IndexOf("// <UserCode>");
			//var userCodeLine = result.SourceCode.Substring(0, userCodeIndex).ToCharArray().Count(x => x == '\r') + 2; // +\r + next line

//			if (userCodeIndex < 1)
//			{
//				return _nonUserCodeSpecialLineNumber;
//			}
//			if (errorLine < userCodeLine)
//			{
//				return _nonUserCodeSpecialLineNumber;
//			}
//			if (userCodeLine < 0)
//			{
//				return _nonUserCodeSpecialLineNumber;
//			}
//			return errorLine -
//				userCodeLine + _ctx.CurrentMacrosLineInOriginalFile;
		}

		string BuildError_GetMessage(CompilerError error, EvaluationResult result)
		{
			// Log a message here. It is not very convinient considering a role of this procedure
			{
				var source = result.SourceCode;
				// Annotate source code with line numbers
				{
					source = string.Join(Environment.NewLine, source.Split('\r').Select((x, i) => (i + 1).ToString("00") + "| " + x.Trim('\n')).ToArray());
				}
				var fullLogEntry = error.ErrorText + " at line " + error.Line + " col " + error.Column + "\r\n" + source;
				_ctx.BuildErrorLogger.LogOutputMessage(fullLogEntry);
			}
			return _metacreatorErrorPrefix + error.ErrorText;
		}

		string BuildError_GetFile(EvaluationResult result, CompilerError error)
		{
//			var orig = _ctx.GetOriginalFileNameRelativeToIntermediatePath();
//			if(result.Errors.First().FileName == orig)
//			{
//				return orig;
//			}
			return error.FileName;
			//if (result.NonUserCode != null || !_ctx.ErrorRemap)
//			{
//				result.NonUserCode = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(_ctx.OriginalFileName) + ".meta" + Path.GetExtension(_ctx.OriginalFileName));
//				File.WriteAllText(result.NonUserCode, result.SourceCode);
//				return result.NonUserCode;
//			}
//			return _ctx.OriginalFileName;
		}

		ProcessFileCtx _ctx;
		IBuildErrorLogger _buildErrorLogger;

		const string _metacreatorErrorPrefix = "MetaCode: ";


	}
}
