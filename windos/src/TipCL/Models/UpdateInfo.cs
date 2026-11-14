using System.Text.Json.Serialization;

namespace TipCL.Models;

public sealed class UpdateInfo
{
	[JsonPropertyName("has_update")]
	public bool HasUpdate { get; set; }

	public string Message { get; set; } = "";

	[JsonPropertyName("latest")]
	public string Version { get; set; } = "";

	public string Notes { get; set; } = "";

	public string Url { get; set; } = "";
}
