﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomNpcs
{
	internal static class DefinitionLoader
	{
		internal static List<T> LoadFromFile<T>(string filePath) where T : DefinitionBase
		{
			List<T> result = null;
			var definitionType = typeof(T);
			var typeName = definitionType.Name;

			if( File.Exists(filePath) )
			{
				var definitions = deserializeFromText<T>(filePath);
				var failedDefinitions = new List<T>();

				foreach( var definition in definitions )
				{
					try
					{
						definition.ThrowIfInvalid();
					}
					catch( FormatException ex )
					{
						CustomNpcsPlugin.Instance.LogPrint($"An error occurred while parsing {typeName} '{definition.Name}': {ex.Message}", TraceLevel.Error);
						failedDefinitions.Add(definition);
					}
					catch( Exception ex )
					{
						CustomNpcsPlugin.Instance.LogPrint($"An error occurred while trying to load {typeName} '{definition.Name}': {ex.Message}", TraceLevel.Error);
						failedDefinitions.Add(definition);
					}
				}

				result = definitions.Except(failedDefinitions).ToList();
			}
			else
			{
				CustomNpcsPlugin.Instance.LogPrint($"Configuration for {typeName} does not exist. Expected config file to be at: {filePath}", TraceLevel.Error);
				result = new List<T>();
			}
			
			return result;
		}

		static List<T> deserializeFromText<T>(string filePath) where T : DefinitionBase
		{
			var expandedDefinitions = new List<T>();

			if( File.Exists(filePath) )
			{
				var json = File.ReadAllText(filePath);
				var definitionType = typeof(T);
				var rawDefinitions = (List<DefinitionBase>)JsonConvert.DeserializeObject(json,
																						typeof(List<DefinitionBase>),
																						new DefinitionOrCategoryJsonConverter(definitionType));
				foreach( var rawDef in rawDefinitions )
				{
					if( rawDef is T )
					{
						//this is a real definition
						expandedDefinitions.Add(rawDef as T);
					}
					else if( rawDef is CategoryPlaceholderDefinition )
					{
						//this is a placeholder definition, which points to included definitions.
						var placeholder = rawDef as CategoryPlaceholderDefinition;
						var includedDefinitions = placeholder.TryLoadIncludes<T>(filePath);

						expandedDefinitions.AddRange(includedDefinitions);
					}
					//else
					//{
					//	//throw?
					//}
				}
			}

			return expandedDefinitions;
		}
	}
}