﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using MetaCreator.AppDomainIsolation;
using MetaCreator.Utils;

namespace MetaCreator.Evaluation
{
	/// <summary>
	/// Inmemory code executor. Consider to execute this in separate app domain.
	/// </summary>
	class Code3Runer : IMetaEngine
	{
		EvaluationResult _evaluationResult;

		internal void Run(EvaluationResult evaluationResult, string className, string methodName)
		{
			_evaluationResult = evaluationResult;

			evaluationResult.EnsureExistsDebug();
			evaluationResult.Assembly.EnsureExistsDebug();

			var type = evaluationResult.Assembly.GetType(className, true);
			type.EnsureExistsDebug("Generator class not found");

			var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
			method.EnsureExistsDebug("Generator method not found");

			if (method.GetParameters().Count() != 0 || method.GetGenericArguments().Count() != 0)
			{
				throw new Exception("Method has unexpected parameters");
			}

			object returnedValue = null;
			using (new Resolver(evaluationResult.References))
			{
				try
				{
					object instance = null;
					if (!method.IsStatic)
					{
						instance = Activator.CreateInstance(type, new object[] { this });
					}
					returnedValue = method.Invoke(instance, null);
				}
				catch (TargetInvocationException ex)
				{
					evaluationResult.EvaluationException = ex.InnerException ?? ex;
				}
			}

			evaluationResult.ReturnedValue = returnedValue;
		}

		void IMetaEngine.AddToCompile(string fileContent)
		{
			_evaluationResult.AddToCompile(false, null, fileContent);
		}

		void IMetaEngine.AddToCompile(string fileName, string fileContent)
		{
			_evaluationResult.AddToCompile(false, fileName, fileContent);
		}

		void IMetaEngine.AddToCompile(bool fileInProject, string fileName, string fileContent)
		{
			_evaluationResult.AddToCompile(fileInProject, fileName, fileContent);
		}
	}
}
