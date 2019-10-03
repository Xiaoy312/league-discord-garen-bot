<Query Kind="Program">
  <Reference>&lt;RuntimeDirectory&gt;\Microsoft.Transactions.Bridge.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\SMDiagnostics.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.Configuration.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.DirectoryServices.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.EnterpriseServices.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.IdentityModel.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.IdentityModel.Selectors.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.Messaging.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.Net.Http.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.Runtime.DurableInstancing.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.Runtime.Serialization.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.Security.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.ServiceModel.Activation.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.ServiceModel.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.ServiceModel.Internals.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.ServiceProcess.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.Web.ApplicationServices.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.Web.Services.dll</Reference>
  <Reference>&lt;RuntimeDirectory&gt;\System.Xaml.dll</Reference>
  <NuGetReference Version="0.9.11">AngleSharp</NuGetReference>
  <NuGetReference>Newtonsoft.Json</NuGetReference>
  <NuGetReference>Splat</NuGetReference>
  <Namespace>AngleSharp</Namespace>
  <Namespace>AngleSharp.Dom</Namespace>
  <Namespace>AngleSharp.Dom.Html</Namespace>
  <Namespace>AngleSharp.Parser.Html</Namespace>
  <Namespace>Html = AngleSharp.Dom.Html</Namespace>
  <Namespace>Splat</Namespace>
  <Namespace>System</Namespace>
  <Namespace>System.Collections.Specialized</Namespace>
  <Namespace>System.Net</Namespace>
  <Namespace>System.Security.Cryptography</Namespace>
  <Namespace>Newtonsoft.Json</Namespace>
</Query>

void Main()
{
	// CachingServiceTest.RunAllTests();
	RegisterServices();

	DownloadAramBuilds();
	// ParseAramBuilds();
	// DownloadSummmonerRiftBuilds
	// ParseSummmonerRiftBuilds();
}

// Define other methods and classes here
void RegisterServices()
{
	Locator.CurrentMutable.Register<ICachingService>(() => new MetaSrcCachingService(Path.Combine(Util.CurrentQuery.Location, "cache")));
}

void DownloadAramBuilds()
{
	var cachingService = Locator.Current.GetService<ICachingService>();

	var builds = cachingService.GetParsedDocument(new Uri("https://www.metasrc.com/aram/champions"))
		.QuerySelectorAll<IHtmlTableCellElement>("#stats-table > tbody > tr > td:nth-child(1)")
		.Select(x => Regex.Match(x.GetAttribute("onclick"), "href='(?<build>.+)'").Groups["build"].Value)
		.Select(x => new Uri(MetaSrcCachingService.SiteBase, x))
		.ToArray();

	var container = new DumpContainer().Dump();
	var pb = new Util.ProgressBar(hideWhenCompleted: true).Dump();
	foreach (var build in builds.AsIndexed())
	{
		container.Content = Util.Metatext(build.Value.LocalPath);

		cachingService.GetCachedFile(build.Value);

		pb.Caption = $"Downloaded {build.Index + 1} out of {builds.Length}";
		pb.Fraction = build.Index / (double)builds.Length;
	}
}
void ParseAramBuilds()
{
	var cachingService = Locator.Current.GetService<ICachingService>();

	var builds = cachingService.GetParsedDocument(new Uri("https://www.metasrc.com/aram/champions"))
		.QuerySelectorAll<IHtmlTableCellElement>("#stats-table > tbody > tr > td:nth-child(1)")
		.Select(x => Regex.Match(x.GetAttribute("onclick"), "href='(?<build>.+)'").Groups["build"].Value)
		.Select(x => new Uri(MetaSrcCachingService.SiteBase, x))
		//.Take(1)
		.Select(build =>
		{
			var document = cachingService.GetParsedDocument(build);
			
			var champion = document.QuerySelector("#splash-content > div:nth-child(1) > div:nth-child(2) > div").TextContent;
			var avatar = document.QuerySelector<IHtmlImageElement>("#splash-content > div:nth-child(1) > div > img").Dataset["cfsrc"];
			var roles = string.Join(", ", document.QuerySelectorAll("#splash-content > div:nth-child(1) > div:nth-child(2) > div:nth-child(3) > Span").Select(x => x.TextContent));
			
			var rows = document.QuerySelectorAll<IHtmlDivElement>("#main-content > div:nth-child(1) > div > div.col-xs-12 > div")
				.Take(2)
				.ToArray();
			var summoners = rows[0].QuerySelectorAll("> div.col-xs:nth-child(1) > div:nth-child(2) > div")
				.ToDictionary(x => x.FirstElementChild.GetAttribute("alt"), x => x.LastElementChild.TextContent);
			var startingItems = rows[0].QuerySelectorAll("> div.col-xs:nth-child(2) > div:nth-child(2) > div")
				.ToDictionary(x => x.FirstElementChild.GetAttribute("alt"), x => x.LastElementChild.TextContent);
			var items = rows[0].QuerySelectorAll("> div.col-xs:nth-child(3) > div:nth-child(2) > div")
				.ToDictionary(x => x.FirstElementChild.GetAttribute("alt"), x => x.LastElementChild.TextContent);
									
			var skills = rows[1].QuerySelectorAll<IHtmlTableRowElement>("> div.col-xs:nth-child(1) > div > table > tbody > tr")
				.Skip(1) // skip passive row
				.Select(tr => tr.Cells
					.Skip(1) // skip skill icon column
					.Select(x => x.TextContent.Length == 1 ? x.TextContent : null))
				.Zip("QWER", Tuple.Create) // some champion doesnt R apparently, oops
				.ToDictionary(tuple => tuple.Item2.ToString(), tuple => tuple.Item1.ToArray());
			var runes = rows[1].QuerySelectorAll("> div.col-xs:nth-child(2) > span > div > div[title]")
				.Select(x => x.GetAttribute("title"))
				.Select(x => Regex.Match(x, @">(?<header>[^<>]+)</div><span", RegexOptions.ExplicitCapture).Groups["header"].Value)
				.ToList();
	
			return new ChampionBuild
			{
				Champion = champion,
				AvatarUrl = avatar,
				Mode = "aram",
				Lane = "mid",
				Roles = roles,
			
				Summoners = summoners.ToDictionary(x => x.Key, x => Percentage.Parse(x.Value)),
				StartingItems = startingItems.ToDictionary(x => x.Key, x => Percentage.Parse(x.Value)),
				Items = items.ToDictionary(x => x.Key, x => Percentage.Parse(x.Value)),
				
				SkillOrder = skills,
				RunePage = new RunePage
				{
					PrimaryPath = runes[0],
					Keystone = runes[1],
					Primary = runes.GetRange(2, 3),
					SecondaryPath = runes[5],
					Secondary = runes.GetRange(6, 2),
					Shards = runes.GetRange(8, 3),
				}
			};
		}).ToArray().Dump();

	var json = JsonConvert.SerializeObject(builds);
	File.WriteAllText(Path.Combine(Util.CurrentQuery.Location, "aram.json"), json);
}
void DownloadSummmonerRiftBuilds()
{
	var cachingService = Locator.Current.GetService<ICachingService>();

	var builds = cachingService.GetParsedDocument(new Uri("https://www.metasrc.com/5v5/champions"))
		.QuerySelectorAll("#stats-table > tbody > tr > td:nth-child(1)")
		.Select(x => Regex.Match(x.GetAttribute("onclick"), "href='(?<build>.+)'").Groups["build"].Value)
		.Select(x => new Uri(MetaSrcCachingService.SiteBase, x))
		.ToArray();

	var container = new DumpContainer().Dump();
	var pb = new Util.ProgressBar(hideWhenCompleted: true).Dump();
	foreach (var build in builds.AsIndexed())
	{
		container.Content = Util.Metatext(build.Value.LocalPath);

		cachingService.GetCachedFile(build.Value);

		pb.Caption = $"Downloaded {build.Index + 1} out of {builds.Length}";
		pb.Fraction = build.Index / (double)builds.Length;
	}
}
void ParseSummmonerRiftBuilds()
{
	var cachingService = Locator.Current.GetService<ICachingService>();

	var primaryLanes = cachingService.GetParsedDocument(new Uri("https://www.metasrc.com/5v5/champions"))
		.QuerySelectorAll<IHtmlTableRowElement>("#stats-table > tbody > tr")
		.Select(row => new
		{
			Champion = row.Cells[0].FirstChild.TextContent.ToLower(),
			Lane = row.Cells[1].TextContent.ToLower(),
			RoleRate = Percentage.Parse(row.Cells[6].TextContent),
		})
		.OrderBy(x => x.Champion)
		.ThenByDescending(x => x.RoleRate)
		.GroupBy(x => x.Champion, (k, g) => g.FirstOrDefault())
		.ToDictionary(x => x.Champion, x => x.Lane);
	var builds = cachingService.GetParsedDocument(new Uri("https://www.metasrc.com/5v5/champions"))
		.QuerySelectorAll("#stats-table > tbody > tr > td:nth-child(1)")
		.Select(x => Regex.Match(x.GetAttribute("onclick"), "href='(?<build>.+)'").Groups["build"].Value)
		.Select(x => new Uri(MetaSrcCachingService.SiteBase, x))
//		.Take(5)
		.Select(build =>
		{
			var document = cachingService.GetParsedDocument(build);

			var champion = document.QuerySelector("#splash-content > div:nth-child(1) > div:nth-child(2) > div").TextContent;
			var avatar = document.QuerySelector<IHtmlImageElement>("#splash-content > div:nth-child(1) > div > img").Dataset["cfsrc"];
			var roles = string.Join(", ", document.QuerySelectorAll("#splash-content > div:nth-child(1) > div:nth-child(2) > div:nth-child(3) > Span").Select(x => x.TextContent));
			var lane = build.Segments.LastOrDefault();

			var rows = document.QuerySelectorAll<IHtmlDivElement>("#main-content > div:nth-child(1) > div > div.col-xs-12 > div")
				.Take(2)
				.ToArray();
			var summoners = rows[0].QuerySelectorAll("> div.col-xs:nth-child(1) > div:nth-child(2) > div")
				.ToDictionary(x => x.FirstElementChild.GetAttribute("alt"), x => x.LastElementChild.TextContent);
			var startingItems = rows[0].QuerySelectorAll("> div.col-xs:nth-child(2) > div:nth-child(2) > div")
				.ToDictionary(x => x.FirstElementChild.GetAttribute("alt"), x => x.LastElementChild.TextContent);
			var items = rows[0].QuerySelectorAll("> div.col-xs:nth-child(3) > div:nth-child(2) > div")
				.ToDictionary(x => x.FirstElementChild.GetAttribute("alt"), x => x.LastElementChild.TextContent);

			var skills = rows[1].QuerySelectorAll<IHtmlTableRowElement>("> div.col-xs:nth-child(1) > div > table > tbody > tr")
				.Skip(1) // skip passive row
				.Select(tr => tr.Cells
					.Skip(1) // skip skill icon column
					.Select(x => x.TextContent.Length == 1 ? x.TextContent : null))
				.Zip("QWER", Tuple.Create) // some champion doesnt R apparently, oops
				.ToDictionary(tuple => tuple.Item2.ToString(), tuple => tuple.Item1.ToArray());
			var runes = rows[1].QuerySelectorAll("> div.col-xs:nth-child(2) > span > div > div[title]")
				.Select(x => x.GetAttribute("title"))
				.Select(x => Regex.Match(x, @">(?<header>[^<>]+)</div><span", RegexOptions.ExplicitCapture).Groups["header"].Value)
				.ToList();

			return new ChampionBuild
			{
				Champion = champion,
				AvatarUrl = avatar,
				Mode = "5v5",
				Lane = lane,
				IsDefaultLane = primaryLanes[champion.ToLower()] == lane,
				Roles = roles,

				Summoners = summoners.ToDictionary(x => x.Key, x => Percentage.Parse(x.Value)),
				StartingItems = startingItems.ToDictionary(x => x.Key, x => Percentage.Parse(x.Value)),
				Items = items.ToDictionary(x => x.Key, x => Percentage.Parse(x.Value)),

				SkillOrder = skills,
				RunePage = new RunePage
				{
					PrimaryPath = runes[0],
					Keystone = runes[1],
					Primary = runes.GetRange(2, 3),
					SecondaryPath = runes[5],
					Secondary = runes.GetRange(6, 2),
					Shards = runes.GetRange(8, 3),
				}
			};
		}).ToArray().Dump();

	var json = JsonConvert.SerializeObject(builds);
	File.WriteAllText(Path.Combine(Util.CurrentQuery.Location, "5v5.json"), json);
}

public class ChampionBuild
{
	public string Champion { get; set; }
	public string AvatarUrl { get; set; }
	public string Mode { get; set; }
	public string Lane { get; set; }
	public bool IsDefaultLane { get; set; }
	public string Roles { get; set; }

	public Dictionary<string, double> Summoners { get; set; }
	public Dictionary<string, double> StartingItems { get; set; }
	public Dictionary<string, double> Items { get; set; }
	public Dictionary<string, string[]> SkillOrder { get; set; }
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

public interface ICachingStrategy
{
	ICachingStrategyMatch Match(Uri uri);
	string BuildPath(NameValueCollection parameters);
}
public interface ICachingStrategyMatch
{
	bool Success { get; }
	NameValueCollection Variables { get; }
}
public interface ICachingService
{
	string ComputePath(Uri uri, NameValueCollection hints = null);
	string GetCachedFile(Uri uri, NameValueCollection hints = null);

	void DeleteCache(Uri uri, NameValueCollection hints = null);
	void DeleteAllCache();
}

public class CachingStrategy : ICachingStrategy
{
	public static readonly string CacheDirectory = Path.Combine(Path.GetDirectoryName(Util.CurrentQueryPath), "cache");
	public static readonly Uri CacheAddress = new Uri(CacheDirectory);

	private Func<Uri, ICachingStrategyMatch> matcher;
	private Func<Uri, NameValueCollection> parser;
	private UriTemplate cachePath;

	public CachingStrategy(UriTemplate template, Uri baseUri, string cachePath)
	{
		this.matcher = x => new UriTemplateMatchResult(template.Match(baseUri, x));
		this.cachePath = new UriTemplate(cachePath);
	}

	public ICachingStrategyMatch Match(Uri uri) => matcher(uri);
	public string BuildPath(NameValueCollection parameters) => cachePath.BindByName(CacheAddress, parameters).LocalPath;
}
public abstract class CachingStrategyMatchBase<T> : ICachingStrategyMatch
{
	public T Match { get; }

	public CachingStrategyMatchBase(T match)
	{
		this.Match = match;
	}

	public abstract bool Success { get; }
	public abstract NameValueCollection Variables { get; }
}
public class UriTemplateMatchResult : CachingStrategyMatchBase<UriTemplateMatch>
{
	public UriTemplateMatchResult(UriTemplateMatch match) : base(match) { }

	public override bool Success => this.Match != null;
	public override NameValueCollection Variables => this.Match.BoundVariables;
}
public abstract class CachingServiceBase : ICachingService
{
	public string CacheDirectory { get; }
	public Uri CacheAddress { get; }

	protected IReadOnlyCollection<ICachingStrategy> strategies;

	public CachingServiceBase(string cacheDirectory, params ICachingStrategy[] strategies)
	{
		this.CacheDirectory = cacheDirectory;
		this.CacheAddress = new Uri(CacheDirectory);
		this.strategies = strategies;
	}

	public string ComputePath(Uri uri, NameValueCollection hints = null)
	{
		var result = strategies.SelectWith(x => x.Match(uri)).FirstOrDefault(x => x.Result.Success);
		if (result == default)
			throw new ArgumentException("No match for: " + uri);

		var parameters = result.Result.Variables;
		parameters.Write(hints ?? new NameValueCollection());

		var path = result.Item.BuildPath(parameters);

		return path;
	}
	public string GetCachedFile(Uri uri, NameValueCollection hints = null)
	{
		var path = ComputePath(uri, hints);
		if (!File.Exists(path))
		{
			if (!Directory.Exists(Path.GetDirectoryName(path)))
				Directory.CreateDirectory(Path.GetDirectoryName(path));

			var client = new WebClient() { Encoding = Encoding.UTF8 };
			Util.Metatext("Downloading: " + uri).Dump();
			client.DownloadFile(uri, path);
		}

		return path;
	}

	public void DeleteCache(Uri uri, NameValueCollection hints = null) { }
	public void DeleteAllCache() { }
}
public class MetaSrcCachingService : CachingServiceBase
{
	public static readonly Uri SiteBase = new Uri("https://www.metasrc.com");

	public MetaSrcCachingService(string cacheDirectory) : base(cacheDirectory, CreateCachingStrategies()) { }
	private static ICachingStrategy[] CreateCachingStrategies()
	{
		return new ICachingStrategy[]
		{
			Create("{mode}", "{mode}/index.html"),
			Create("{mode}/champions", "{mode}/champions.html"),
			Create("{mode}/champion/{champion}", "{mode}/{champion}.html"),
			Create("{mode}/champion/{champion}/{role}", "{mode}/{champion}-{role}.html"),
		};

		ICachingStrategy Create(string template, string cachePath) => new CachingStrategy(new UriTemplate(template, true), SiteBase, cachePath);
	}
}

public class CachingServiceTest
{
	public static void RunAllTests()
	{
		CachedPathShouldBeCorrect();
	}

	public static void CachedPathShouldBeCorrect()
	{
		var cacheDirectory = Path.Combine(Util.CurrentQuery.Location, "cache");
		var service = new MetaSrcCachingService(cacheDirectory);
		var facts = new Dictionary<string, string>
		{
			["https://www.metasrc.com/5v5"] = Path.Combine(cacheDirectory, "5v5", "index.html"),
			["https://www.metasrc.com/arurf"] = Path.Combine(cacheDirectory, "arurf", "index.html"),
			["https://www.metasrc.com/arurf/champions"] = Path.Combine(cacheDirectory, "arurf", "champions.html"),
			["https://www.metasrc.com/aram/champion/jhin"] = Path.Combine(cacheDirectory, "aram", "jhin.html"),
			["https://www.metasrc.com/aram/champion/zyra"] = Path.Combine(cacheDirectory, "aram", "zyra.html"),
			["https://www.metasrc.com/5v5/champion/jhin/adc"] = Path.Combine(cacheDirectory, "5v5", "jhin-adc.html"),
			["https://www.metasrc.com/5v5/champion/zyra/support"] = Path.Combine(cacheDirectory, "5v5", "zyra-support.html"),
			["https://www.metasrc.com/blitz/champion/jhin/lane"] = Path.Combine(cacheDirectory, "blitz", "jhin-lane.html"),
			["https://www.metasrc.com/blitz/champion/jhin/jungle"] = Path.Combine(cacheDirectory, "blitz", "jhin-jungle.html"),
		};

		facts
			.SelectWithAttempt(x => service.ComputePath(new Uri(x.Key)))
			.Select(x => new
			{
				Address = x.Item.Key,
				Result = x.Attempt.Success ? (object)x.Attempt.Result : Util.OnDemand("failed", () => x.Attempt.Exception),
				Expectation = Util.OnDemand("expand", () => x.Item.Value),
				Success = Util.HighlightIf(x.Attempt.Success && x.Attempt.Result == x.Item.Value, y => !y),
			})
			.Dump(MethodBase.GetCurrentMethod().Name);
	}
}

public static class Percentage
{
	public static double Parse(string value) => double.Parse(value.TrimEnd('%')) / 100d;
}

public static class EnumerableExtensions
{
	public static IEnumerable<(T Item, TResult Result)> SelectWith<T, TResult>(this IEnumerable<T> source, Func<T, TResult> selector)
	{
		return source.Select(x => (x, selector(x)));
	}
	public static IEnumerable<(T Item, (bool Success, TResult Result, Exception Exception) Attempt)> SelectWithAttempt<T, TResult>(this IEnumerable<T> source, Func<T, TResult> selector)
	{
		return source.Select(x => (x, Attempt(x)));

		(bool success, TResult result, Exception error) Attempt(T x)
		{
			try
			{
				return (true, selector(x), default);
			}
			catch (Exception ex)
			{
				return (false, default, ex);
			}
		}
	}
	public static IEnumerable<(T Value, int Index)> AsIndexed<T>(this IEnumerable<T> source)
	{
		return source.Select((x, i) => (x, i));
	}
}
public static class DictionaryExtensions
{
	public static bool IsDefault<TKey, TValue>(this KeyValuePair<TKey, TValue> pair)
	{
		return pair.Equals(default(KeyValuePair<TKey, TValue>));
	}
	public static NameValueCollection ToNameValueCollection(this Dictionary<string, string> dictionary)
	{
		var collection = new NameValueCollection();
		foreach (var pair in dictionary)
			collection.Add(pair.Key, pair.Value);

		return collection;
	}
}
public static class NameValueCollectionExtensions
{
	public static NameValueCollection Write(this NameValueCollection collection, NameValueCollection other)
	{
		foreach (string key in other)
			collection[key] = other[key];

		return collection;
	}
}

public static class QuerySelectorExtensions
{
	public static TElement QuerySelector<TElement>(this IParentNode node, string selectors)
		where TElement : IElement
	{
		return (TElement)node.QuerySelector(selectors);
	}
	public static IEnumerable<TElement> QuerySelectorAll<TElement>(this IParentNode node, string selectors)
		where TElement : IElement
	{
		return node.QuerySelectorAll(selectors).Cast<TElement>();
	}
}

public static class CachingServiceExtensions
{
	public static string GetSource(this ICachingService service, Uri uri, NameValueCollection hints = null) => File.ReadAllText(service.GetCachedFile(uri, hints));
	public static IDocument GetParsedDocument(this ICachingService service, Uri uri, NameValueCollection hints = null, bool requireAbsoluteHref = false)
	{
		return !requireAbsoluteHref
			? new HtmlParser().Parse(service.GetSource(uri, hints))
			: BrowsingContext.New().OpenAsync(res => res
				.Content(service.GetSource(uri, hints))
				.Address(uri)
				.Status(HttpStatusCode.OK)
			).Result;
	}
}