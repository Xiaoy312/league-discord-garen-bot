<Query Kind="Statements">
  <NuGetReference>Microsoft.Extensions.Configuration.Json</NuGetReference>
  <Namespace>Microsoft.Extensions.Configuration</Namespace>
</Query>

var config = new ConfigurationBuilder()
			.SetBasePath(Util.CurrentQuery.Location)
			.AddJsonFile("config.json")
			.Build();

config.GetSection("hideInBushes").GetChildren().Select(x => x["channelId"])
	.Dump();