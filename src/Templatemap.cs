using CleanArchitecture.CodeGenerator.Helpers;
using CleanArchitecture.CodeGenerator.Models;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CleanArchitecture.CodeGenerator;

internal static class TemplateMap
{
	private static readonly List<string> TemplateFiles = new();
	private const string DefaultExt = ".txt";
	private const string TemplateDir = ".templates";
	private const string DefaultNamespace = "";
	static TemplateMap()
	{
		string folder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		string userProfile = Path.Combine(folder, ".vs", TemplateDir);

		if (Directory.Exists(userProfile))
		{
			TemplateFiles.AddRange(Directory.GetFiles(userProfile, "*" + DefaultExt, SearchOption.AllDirectories));
		}

		string assembly = Assembly.GetExecutingAssembly().Location;
		string folder1 = Path.Combine(Path.GetDirectoryName(assembly), "Templates");
		TemplateFiles.AddRange(Directory.GetFiles(folder1, "*" + DefaultExt, SearchOption.AllDirectories));
	}


	public static async Task<string> GetTemplateFilePathAsync(Project project, IntellisenseObject classObject, string file, string itemName, string selectFolder)
	{
		string[] templateFolders ={
			"Commands\\AcceptChanges",
			"Commands\\Create",
			"Commands\\Delete",
			"Commands\\Update",
			"Commands\\AddEdit",
			"Commands\\Import",
			"DTOs",
			"Caching",
			"EventHandlers",
			"Events",
			"Queries\\Export",
			"Queries\\GetAll",
			"Queries\\GetById",
			"Queries\\Pagination",
			"Pages",
			"Pages\\Components",
			"Persistence\\Configurations",
		};
		string extension = Path.GetExtension(file).ToLowerInvariant();
		string name = Path.GetFileName(file);
		string safeName = name.StartsWith(".") ? name : Path.GetFileNameWithoutExtension(file);
		string relative = PackageUtilities.MakeRelative(project.GetRootFolder(), Path.GetDirectoryName(file) ?? "");
		string selectRelative = PackageUtilities.MakeRelative(project.GetRootFolder(), selectFolder ?? "");
		string templateFile = null;
		List<string> list = TemplateFiles.ToList();

		AddTemplatesFromCurrentFolder(list, Path.GetDirectoryName(file));

		// Look for direct file name matches
		if (list.Any(f => {
			string pattern = templateFolders.First(x => relative.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0).Replace("\\", "\\\\");
			bool result = Regex.IsMatch(f, pattern, RegexOptions.IgnoreCase);
			return result;

		}))
		{
			string tmplFile = list.OrderByDescending(x => x.Length).FirstOrDefault(f => {
				string pattern = templateFolders.First(x => relative.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0).Replace("\\", "\\\\"); ;
				bool result = Regex.IsMatch(f, pattern, RegexOptions.IgnoreCase);
				if (result)
				{
					return Path.GetFileNameWithoutExtension(f).Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries).All(x => name.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0);
				}
				return false;
			});
			templateFile = tmplFile;  //Path.Combine(Path.GetDirectoryName(tmplFile), name + _defaultExt);//GetTemplate(name);
		}

		// Look for file extension matches
		else if (list.Any(f => Path.GetFileName(f).Equals(extension + DefaultExt, StringComparison.OrdinalIgnoreCase)))
		{
			string tmplFile = list.FirstOrDefault(f => Path.GetFileName(f).Equals(extension + DefaultExt, StringComparison.OrdinalIgnoreCase) && File.Exists(f));
			string tmpl = AdjustForSpecific(safeName, extension);
			templateFile = Path.Combine(Path.GetDirectoryName(tmplFile), tmpl + DefaultExt); //GetTemplate(tmpl);
		}
		string template = await ReplaceTokensAsync(project, classObject, itemName, relative, selectRelative, templateFile);
		return NormalizeLineEndings(template);
	}

	private static void AddTemplatesFromCurrentFolder(List<string> list, string dir)
	{
		DirectoryInfo current = new(dir);
		List<string> dynaList = new();
		while (current != null)
		{
			string tmplDir = Path.Combine(current.FullName, TemplateDir);

			if (Directory.Exists(tmplDir))
			{
				dynaList.AddRange(Directory.GetFiles(tmplDir, "*" + DefaultExt, SearchOption.AllDirectories));
			}
			current = current.Parent;
		}
		list.InsertRange(0, dynaList);
	}

	private static async Task<string> ReplaceTokensAsync(Project project, IntellisenseObject classObject, string name, string relative, string selectRelative, string templateFile)
	{
		if (string.IsNullOrEmpty(templateFile))
		{
			return templateFile;
		}

		string rootNs = project.GetRootNamespace();
		string ns = string.IsNullOrEmpty(rootNs) ? "MyNamespace" : rootNs;
		string selectNs = ns;
		if (!string.IsNullOrEmpty(relative))
		{
			ns += "." + ProjectHelpers.CleanNameSpace(relative);
		}
		if (!string.IsNullOrEmpty(selectRelative))
		{
			selectNs += "." + ProjectHelpers.CleanNameSpace(selectRelative);
			selectNs = selectNs.Remove(selectNs.Length - 1);
		}

		using StreamReader reader = new(templateFile);
		string content = await reader.ReadToEndAsync();
		string nameofPlural = ProjectHelpers.Pluralize(name);
		string dtoFieldDefinition = CreateDtoFieldDefinition(classObject);
		string importFuncExpression = CreateImportFuncExpression(classObject);
		string templateFieldDefinition = CreateTemplateFieldDefinition(classObject);
		string exportFuncExpression = CreateExportFuncExpression(classObject);
		string mudTdDefinition = CreateMudTdDefinition(classObject);
		string mudTdHeaderDefinition = CreateMudTdHeaderDefinition(classObject);
		string mudFormFieldDefinition = CreateMudFormFieldDefinition(classObject);
		string fieldAssignmentDefinition = CreateFieldAssignmentDefinition(classObject);
		return content.Replace("{rootnamespace}", DefaultNamespace)
					  .Replace("{namespace}", ns)
					  .Replace("{selectns}", selectNs)
					  .Replace("{itemname}", name)
					  .Replace("{nameofPlural}", nameofPlural)
					  .Replace("{dtoFieldDefinition}", dtoFieldDefinition)
					  .Replace("{fieldAssignmentDefinition}", fieldAssignmentDefinition)
					  .Replace("{importFuncExpression}", importFuncExpression)
					  .Replace("{templateFieldDefinition}", templateFieldDefinition)
					  .Replace("{exportFuncExpression}", exportFuncExpression)
					  .Replace("{mudTdDefinition}", mudTdDefinition)
					  .Replace("{mudTdHeaderDefinition}", mudTdHeaderDefinition)
					  .Replace("{mudFormFieldDefinition}", mudFormFieldDefinition)
			;
	}

	private static string NormalizeLineEndings(string content)
	{
		if (string.IsNullOrEmpty(content))
		{
			return content;
		}

		return Regex.Replace(content, @"\r\n|\n\r|\n|\r", "\r\n");
	}

	private static string AdjustForSpecific(string safeName, string extension)
	{
		if (Regex.IsMatch(safeName, "^I[A-Z].*"))
		{
			return extension + "-interface";
		}

		return extension;
	}
	private static string SplitCamelCase(string str)
	{
		Regex r = new(@"
                (?<=[A-Z])(?=[A-Z][a-z]) |
                 (?<=[^A-Z])(?=[A-Z]) |
                 (?<=[A-Za-z])(?=[^A-Za-z])", RegexOptions.IgnorePatternWhitespace);
		return r.Replace(str, " ");
	}
	public const string Primarykey = "Id";
	private static string CreateDtoFieldDefinition(IntellisenseObject classObject)
	{
		StringBuilder output = new();
		foreach (IntellisenseProperty property in classObject.Properties.Where(x => x.Type.IsKnownType))
		{
			output.Append($"    [Description(\"{SplitCamelCase(property.Name)}\")]\r\n");
			if (property.Name == Primarykey)
			{
				output.Append($"    public {property.Type.CodeName} {property.Name} {{get;set;}} \r\n");
			}
			else
			{
				switch (property.Type.CodeName)
				{
					case "string" when property.Name.Equals("Name", StringComparison.OrdinalIgnoreCase):
						output.Append($"    public {property.Type.CodeName} {property.Name} {{get;set;}} = string.Empty; \r\n");
						break;
					case "string" when !property.Name.Equals("Name", StringComparison.OrdinalIgnoreCase) && !property.Type.IsArray && !property.Type.IsDictionary:
						output.Append($"    public {property.Type.CodeName}? {property.Name} {{get;set;}} \r\n");
						break;
					case "string" when !property.Name.Equals("Name", StringComparison.OrdinalIgnoreCase) && property.Type.IsArray:
						output.Append($"    public HashSet<{property.Type.CodeName}>? {property.Name} {{get;set;}} \r\n");
						break;
					case "System.DateTime?":
						output.Append($"    public DateTime? {property.Name} {{get;set;}} \r\n");
						break;
					case "System.DateTime":
						output.Append($"    public DateTime {property.Name} {{get;set;}} \r\n");
						break;
					case "decimal?":
					case "decimal":
					case "int?":
					case "int":
					case "double?":
					case "double":
						output.Append($"    public {property.Type.CodeName} {property.Name} {{get;set;}} \r\n");
						break;
					default:
						if (property.Type.CodeName.Any(x => x == '?'))
						{
							output.Append($"    public {property.Type.CodeName} {property.Name} {{get;set;}} \r\n");
						}
						else
						{
							if (property.Type.IsOptional)
							{
								output.Append($"    public {property.Type.CodeName}? {property.Name} {{get;set;}} \r\n");
							}
							else
							{
								output.Append($"    public {property.Type.CodeName} {property.Name} {{get;set;}} \r\n");
							}
						}
						break;
				}

			}
		}
		return output.ToString();
	}
	private static string CreateImportFuncExpression(IntellisenseObject classObject)
	{
		StringBuilder output = new();
		foreach (IntellisenseProperty property in classObject.Properties.Where(x => x.Type.IsKnownType))
		{
			if (property.Name == Primarykey) continue;
			output.Append(property.Type.CodeName.StartsWith("bool")
							  ? $"{{ _dto.GetMemberDescription(u => u.{property.Name}), (row, item) => item.{property.Name} = Convert.ToBoolean(row[_dto.GetMemberDescription(u => u.{property.Name})]) }}, \r\n"
							  : $"{{ _dto.GetMemberDescription(u => u.{property.Name}), (row, item) => item.{property.Name} = row[_dto.GetMemberDescription(u => u.{property.Name})]?.ToString() }}, \r\n");
		}
		return output.ToString();
	}
	private static string CreateTemplateFieldDefinition(IntellisenseObject classObject)
	{
		StringBuilder output = new();
		foreach (IntellisenseProperty property in classObject.Properties.Where(x => x.Type.IsKnownType))
		{
			if (property.Name == Primarykey) continue;
			output.Append($"_dto.GetMemberDescription(u => u.{property.Name}), \r\n");
		}
		return output.ToString();
	}
	private static string CreateExportFuncExpression(IntellisenseObject classObject)
	{
		StringBuilder output = new();
		foreach (IntellisenseProperty property in classObject.Properties.Where(x => x.Type.IsKnownType))
		{
			output.Append($"{{_dto.GetMemberDescription(u => u.{property.Name}) ,item => item.{property.Name}}}, \r\n");
		}
		return output.ToString();
	}

	private static string CreateMudTdHeaderDefinition(IntellisenseObject classObject)
	{
		StringBuilder output = new();
		string[] defaultFieldName = { "Name", "Description" };
		if (classObject.Properties.Any(x => x.Type.IsKnownType && defaultFieldName.Contains(x.Name)))
		{
			if (classObject.Properties.Any(x => x.Type.IsKnownType && x.Name == defaultFieldName.First()))
			{
				output.Append($"<MudTh><MudTableSortLabel SortLabel=\"Name\" T=\"{classObject.Name}Dto\">@L[_currentDto.GetMemberDescription(\"Name\")]</MudTableSortLabel></MudTh> \r\n");
			}
		}
		foreach (IntellisenseProperty property in classObject.Properties.Where(x => x.Type.IsKnownType && !defaultFieldName.Contains(x.Name)))
		{
			if (property.Name == Primarykey) continue;
			output.Append("                ");
			output.Append($"<MudTh><MudTableSortLabel SortLabel=\"{property.Name}\" T=\"{classObject.Name}Dto\">@L[_currentDto.GetMemberDescription(\"{property.Name}\")]</MudTableSortLabel></MudTh> \r\n");
		}
		return output.ToString();
	}

	private static string CreateMudTdDefinition(IntellisenseObject classObject)
	{
		StringBuilder output = new();
		string[] defaultFieldName = { "Name", "Description" };
		if (classObject.Properties.Any(x => x.Type.IsKnownType && defaultFieldName.Contains(x.Name)))
		{
			output.Append("<MudTd HideSmall=\"false\" DataLabel=\"@L[_currentDto.GetMemberDescription(\"Name\")]\"> \r\n");
			output.Append("                ");
			output.Append("    <div class=\"d-flex flex-column\">\r\n");
			if (classObject.Properties.Any(x => x.Type.IsKnownType && x.Name == defaultFieldName.First()))
			{
				output.Append("                ");
				output.Append("        <MudText>@context.Name</MudText>\r\n");
			}
			if (classObject.Properties.Any(x => x.Type.IsKnownType && x.Name == defaultFieldName.Last()))
			{
				output.Append("                ");
				output.Append("        <MudText Typo=\"Typo.body2\">@context.Description</MudText>\r\n");
			}
			output.Append("                ");
			output.Append("    </div>\r\n");
			output.Append("                ");
			output.Append("</MudTd>\r\n");
		}
		foreach (IntellisenseProperty property in classObject.Properties.Where(x => x.Type.IsKnownType && !defaultFieldName.Contains(x.Name)))
		{
			if (property.Name == Primarykey) continue;
			output.Append("                ");
			if (property.Type.CodeName.StartsWith("bool", StringComparison.OrdinalIgnoreCase))
			{
				output.Append($"        <MudTd HideSmall=\"false\" DataLabel=\"@L[_currentDto.GetMemberDescription(\"{property.Name}\")]\" ><MudCheckBox Checked=\"@context.{property.Name}\" ReadOnly></MudCheckBox></MudTd> \r\n");
			}
			else if (property.Type.CodeName.Equals("System.DateTime", StringComparison.OrdinalIgnoreCase))
			{
				output.Append($"        <MudTd HideSmall=\"false\" DataLabel=\"@L[_currentDto.GetMemberDescription(\"{property.Name}\")]\" >@context.{property.Name}.Date.ToString(\"d\")</MudTd> \r\n");
			}
			else if (property.Type.CodeName.Equals("System.DateTime?", StringComparison.OrdinalIgnoreCase))
			{
				output.Append($"        <MudTd HideSmall=\"false\" DataLabel=\"@L[_currentDto.GetMemberDescription(\"{property.Name}\")]\" >@context.{property.Name}?.Date.ToString(\"d\")</MudTd> \r\n");
			}
			else
			{
				output.Append($"        <MudTd HideSmall=\"false\" DataLabel=\"@L[_currentDto.GetMemberDescription(\"{property.Name}\")]\" >@context.{property.Name}</MudTd> \r\n");
			}

		}
		return output.ToString();
	}

	private static string CreateMudFormFieldDefinition(IntellisenseObject classObject)
	{
		StringBuilder output = new();
		foreach (IntellisenseProperty property in classObject.Properties.Where(x => x.Type.IsKnownType))
		{
			if (property.Name == Primarykey) continue;
			switch (property.Type.CodeName.ToLower())
			{
				case "string" when property.Name.Equals("Name", StringComparison.OrdinalIgnoreCase):
					output.Append("<MudItem xs=\"12\" md=\"6\"> \r\n");
					output.Append("                ");
					output.Append($"        <MudTextField Label=\"@L[model.GetMemberDescription(\"{property.Name}\")]\" @bind-Value=\"model.{property.Name}\" For=\"@(() => model.{property.Name})\" Required=\"true\" RequiredError=\"@L[\"{SplitCamelCase(property.Name).ToLower()} is required!\"]\"></MudTextField>\r\n");
					output.Append("                ");
					output.Append("</MudItem> \r\n");
					break;
				case "string" when property.Name.Equals("Description", StringComparison.OrdinalIgnoreCase):
					output.Append("<MudItem xs=\"12\" md=\"6\"> \r\n");
					output.Append("                ");
					output.Append("        <MudTextField Label=\"@L[model.GetMemberDescription(\"Description\")]\" Lines=\"3\" For=\"@(() => model.Description)\" @bind-Value=\"model.Description\"></MudTextField>\r\n");
					output.Append("                ");
					output.Append("</MudItem> \r\n");
					break;
				case "bool?":
				case "bool":
					output.Append("<MudItem xs=\"12\" md=\"6\"> \r\n");
					output.Append("                ");
					output.Append($"        <MudCheckBox Label=\"@L[model.GetMemberDescription(\"{property.Name}\")]\" @bind-Checked=\"model.{property.Name}\" For=\"@(() => model.{property.Name})\" ></MudCheckBox>\r\n");
					output.Append("                ");
					output.Append("</MudItem> \r\n");
					break;
				case "int?":
				case "int":
					output.Append("<MudItem xs=\"12\" md=\"6\"> \r\n");
					output.Append("                ");
					output.Append($"        <MudNumericField  Label=\"@L[model.GetMemberDescription(\"{property.Name}\")]\" @bind-Value=\"model.{property.Name}\" For=\"@(() => model.{property.Name})\" Min=\"0\" Required=\"false\" RequiredError=\"@L[\"{SplitCamelCase(property.Name).ToLower()} is required!\"]\"></MudNumericField >\r\n");
					output.Append("                ");
					output.Append("</MudItem> \r\n");
					break;
				case "decimal?":
				case "decimal":
					output.Append("<MudItem xs=\"12\" md=\"6\"> \r\n");
					output.Append("                ");
					output.Append($"        <MudNumericField  Label=\"@L[model.GetMemberDescription(\"{property.Name}\")]\" @bind-Value=\"model.{property.Name}\" For=\"@(() => model.{property.Name})\" Min=\"0.00m\" Required=\"false\" RequiredError=\"@L[\"{SplitCamelCase(property.Name).ToLower()} is required!\"]\"></MudNumericField >\r\n");
					output.Append("                ");
					output.Append("</MudItem> \r\n");
					break;
				case "double?":
				case "double":
					output.Append("<MudItem xs=\"12\" md=\"6\"> \r\n");
					output.Append("                ");
					output.Append($"        <MudNumericField  Label=\"@L[model.GetMemberDescription(\"{property.Name}\")]\" @bind-Value=\"model.{property.Name}\" For=\"@(() => model.{property.Name})\" Min=\"0.00\" Required=\"false\" RequiredError=\"@L[\"{SplitCamelCase(property.Name).ToLower()} is required!\"]\"></MudNumericField >\r\n");
					output.Append("                ");
					output.Append("</MudItem> \r\n");
					break;
				case "system.datetime?":
					output.Append("<MudItem xs=\"12\" md=\"6\"> \r\n");
					output.Append("                ");
					output.Append($"        <MudDatePicker Label=\"@L[model.GetMemberDescription(\"{property.Name}\")]\" @bind-Date=\"model.{property.Name}\" For=\"@(() => model.{property.Name})\" Required=\"false\" RequiredError=\"@L[\"{SplitCamelCase(property.Name).ToLower()} is required!\"]\"></MudDatePicker>\r\n");
					output.Append("                ");
					output.Append("</MudItem> \r\n");
					break;
				default:
					output.Append("<MudItem xs=\"12\" md=\"6\"> \r\n");
					output.Append("                ");
					output.Append($"        <MudTextField Label=\"@L[model.GetMemberDescription(\"{property.Name}\")]\" @bind-Value=\"model.{property.Name}\" For=\"@(() => model.{property.Name})\" Required=\"false\" RequiredError=\"@L[\"{SplitCamelCase(property.Name).ToLower()} is required!\"]\"></MudTextField>\r\n");
					output.Append("                ");
					output.Append("</MudItem> \r\n");
					break;

			}

		}
		return output.ToString();
	}


	private static string CreateFieldAssignmentDefinition(IntellisenseObject classObject)
	{
		StringBuilder output = new();
		foreach (IntellisenseProperty property in classObject.Properties.Where(x => x.Type.IsKnownType))
		{
			output.Append("        ");
			output.Append($"        {property.Name} = dto.{property.Name}, \r\n");
		}
		return output.ToString();
	}
}