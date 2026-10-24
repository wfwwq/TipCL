using System.Text.Json.Serialization;
using TipCL.Services;

namespace TipCL.Models;

public sealed class VersionRow
{
	public string Version { get; set; } = "";

	public string Type { get; set; } = "";

	public string Date { get; set; } = "";

	[JsonIgnore]
	public string TypeLabel => Type switch
	{
		"release" => I18n.L("正式版"), 
		"snapshot" => I18n.L("快照"), 
		"old_alpha" => I18n.L("远古 Alpha"), 
		"old_beta" => I18n.L("远古 Beta"), 
		_ => Type, 
	};
}
