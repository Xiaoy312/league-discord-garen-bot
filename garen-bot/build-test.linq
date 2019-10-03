<Query Kind="Program">
  <NuGetReference>Newtonsoft.Json</NuGetReference>
  <Namespace>Newtonsoft.Json</Namespace>
</Query>

void Main()
{
	var json = File.ReadAllText(Path.Combine(Util.CurrentQuery.Location, "aram.json"));
	var builds = JsonConvert.DeserializeObject<ChampionBuild[]>(json);
	
	builds.First().Dump();
}

// Define other methods and classes here
public class ChampionBuild
{
	public string Champion { get; set; }
	public string Mode { get; set; }
	public string Roles { get; set; }

	public Dictionary<string, double> Summoners { get; set; }
	public Dictionary<string, double> StartingItems { get; set; }
	public Dictionary<string, double> Items { get; set; }
	public RunePage RunePage { get; set; }
}
public class RunePage
{
	public string PrimaryPath { get; set; }
	public string SecondaryPath { get; set; }

	public string Keystone { get; set; }
	public List<string> Primary { get; set; }
	public List<string> Secondary { get; set; }
	public List<string> Shards { get; set; }
}