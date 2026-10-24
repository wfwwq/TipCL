using System.Text.Json.Serialization;

namespace TipCL.Models;

public sealed class SaveRow
{
	public string Name { get; set; } = "";

	public string Folder { get; set; } = "";

	public string Size { get; set; } = "";

	public string Mtime { get; set; } = "";

	[JsonPropertyName("last_played")]
	public string LastPlayed { get; set; } = "";

	public string Mode { get; set; } = "";

	public string Seed { get; set; } = "";
}
