<Query Kind="Program">
  <NuGetReference>Discord.Net</NuGetReference>
  <NuGetReference>Microsoft.Extensions.Configuration</NuGetReference>
  <NuGetReference>Microsoft.Extensions.Configuration.Json</NuGetReference>
  <NuGetReference>Microsoft.Extensions.Logging</NuGetReference>
  <NuGetReference>Newtonsoft.Json</NuGetReference>
  <Namespace>Discord</Namespace>
  <Namespace>Discord.Commands</Namespace>
  <Namespace>Discord.WebSocket</Namespace>
  <Namespace>Microsoft.Extensions.Configuration</Namespace>
  <Namespace>Microsoft.Extensions.DependencyInjection</Namespace>
  <Namespace>Microsoft.Extensions.Logging</Namespace>
  <Namespace>Newtonsoft.Json</Namespace>
  <Namespace>System.Threading.Tasks</Namespace>
  <Namespace>System.Collections.Concurrent</Namespace>
  <Namespace>Microsoft.Extensions.Logging.Abstractions.Internal</Namespace>
</Query>

public class GarenBot
{
	private DiscordSocketClient _client;
	private IConfiguration _config;

	public async Task MainAsync()
	{
		FixSSLTLS();

		_client = new DiscordSocketClient();
		_config = BuildConfig();

		var services = ConfigureServices();
		services.GetRequiredService<LogService>();
		await services.GetRequiredService<CommandHandlingService>().InitializeAsync();

		Util.Cleanup += async (s, e) => await _client.StopAsync();
		await _client.LoginAsync(TokenType.Bot, _config["token"]);
		await _client.StartAsync();

		await Task.Delay(-1);
	}
	public static void Main(string[] args) => new GarenBot().MainAsync().GetAwaiter().GetResult();

	private void FixSSLTLS()
	{
		// fix: System.Net.WebException: The request was aborted: Could not create SSL/TLS secure channel.
		System.Net.ServicePointManager.Expect100Continue = true;
		System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12 | System.Net.SecurityProtocolType.Ssl3;
	}

	private IConfiguration BuildConfig()
	{
		return new ConfigurationBuilder()
			.SetBasePath(Util.CurrentQuery.Location)
			.AddJsonFile("config.json")
			.Build();
	}
	private IServiceProvider ConfigureServices()
	{
		return new ServiceCollection()
			// Base
			.AddSingleton(_client)
			.AddSingleton<CommandService>()
			.AddSingleton<CommandHandlingService>()
			// Logging
			.AddLogging(ConfigureLogging)
			.AddSingleton<LogService>()
			// Extra
			.AddSingleton(_config)
			// Add additional services here...
			.BuildServiceProvider();
	}
	private void ConfigureLogging(ILoggingBuilder builder) => builder
		.AddProvider(new CustomConsoleLoggerProvider());
}

public class LogService
{
	private readonly ILogger _discordLogger;
	private readonly ILogger _commandsLogger;

	public LogService(DiscordSocketClient discord, CommandService commands, ILoggerFactory loggerFactory)
	{
		_discordLogger = loggerFactory.CreateLogger(discord.GetType());
		_commandsLogger = loggerFactory.CreateLogger(commands.GetType());

		discord.Log += LogDiscord;
		commands.Log += LogCommand;
	}

	private Task LogDiscord(LogMessage message)
	{
		_discordLogger.Log(
			LogLevelFromSeverity(message.Severity),
			0,
			message,
			message.Exception,
			(_1, _2) => message.ToString(prependTimestamp: false));
		return Task.CompletedTask;
	}
	private Task LogCommand(LogMessage message)
	{
		_commandsLogger.Log(
			LogLevelFromSeverity(message.Severity),
			0,
			message,
			message.Exception,
			 // for CommandException, ToString includes the exception, which we want to avoid
			 // since CustomConsoleLogger already log the exception
			(_1, _2) => message.Exception is CommandException command
				? command.Message
				: message.ToString(prependTimestamp: false)
		);
		return Task.CompletedTask;
	}

	private static LogLevel LogLevelFromSeverity(LogSeverity severity) => (LogLevel)(Math.Abs((int)severity - 5));
}
public class CustomConsoleLoggerProvider : ILoggerProvider
{
	private readonly ConcurrentDictionary<string, CustomConsoleLogger> _loggers = new ConcurrentDictionary<string, CustomConsoleLogger>();

	public ILogger CreateLogger(string categoryName) => _loggers.GetOrAdd(categoryName, x => new CustomConsoleLogger(x));
	public void Dispose() { }

	private class CustomConsoleLogger : ILogger
	{
		public readonly string Name;

		public CustomConsoleLogger(string name)
		{
			this.Name = name;
		}

		public IDisposable BeginScope<TState>(TState state) => NullScope.Instance; // does nothing
		public bool IsEnabled(LogLevel logLevel) => true;
		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
		{
			formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
			var message = formatter(state, exception);

			if (!string.IsNullOrEmpty(message) || exception != null)
			{
				WriteMessage(logLevel, Name, eventId.Id, message, exception);
			}
		}

		private void WriteMessage(LogLevel logLevel, string logName, int eventId, string message, Exception exception)
		{
			var level = GetLogLevelString(logLevel);
			var partialName = logName.Split('.').Last();
			
			Console.WriteLine($"{partialName}: {level}: {message}");
			if (exception != null)
			{
				Console.WriteLine();
				Console.WriteLine(PrettifyAsyncException(exception.ToString()));
				Console.WriteLine();
			}
		}
		private string GetLogLevelString(LogLevel logLevel)
		{
			switch (logLevel)
			{
				case LogLevel.Trace: return "trce";
				case LogLevel.Debug: return "dbug";
				case LogLevel.Information: return "info";
				case LogLevel.Warning: return "warn";
				case LogLevel.Error: return "fail";
				case LogLevel.Critical: return "crit";
				default: throw new ArgumentOutOfRangeException(nameof(logLevel));
			}
		}
		private string PrettifyAsyncException(string exception)
		{
			return Regex.Replace(exception, string.Join(@"\s+",
				@"\s+--- End of (stack trace from previous location where exception was thrown|inner exception stack trace) ---",
				@"at System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw\(\)",
				@"at System.Runtime.CompilerServices.TaskAwaiter.HandleNonSuccessAndDebuggerNotification\(Task task\)"
			), string.Empty, RegexOptions.Singleline);
		}
	}
}
public class CommandHandlingService
{
	private readonly IServiceProvider _provider;
	private readonly DiscordSocketClient _discord;
	private readonly CommandService _commands;
	private readonly IConfiguration _config;
	private readonly ILogger _logger;

	public CommandHandlingService(IServiceProvider provider, DiscordSocketClient discord, CommandService commands, IConfiguration config, ILogger<CommandHandlingService> logger)
	{
		_provider = provider;
		_discord = discord;
		_commands = commands;
		_config = config;
		_logger = logger;

		_discord.MessageReceived += MessageReceivedAsync;
		_commands.CommandExecuted += CommandExecutedAsync;
	}

	public async Task InitializeAsync()
	{
		await _commands.AddModulesAsync(Assembly.GetExecutingAssembly(), _provider);
		// Add additional initialization code here...
	}

	private async Task MessageReceivedAsync(SocketMessage rawMessage)
	{
		// Ignore system messages and messages from bots
		if (!(rawMessage is SocketUserMessage message)) return;
		if (message.Source != MessageSource.User) return;

		if (!_config.GetSection("whitelistedChannels").GetChildren().Any(x => x["channelId"].ToString() == message.Channel.Id.ToString()))
			return;

		_logger.LogInformation($"{message.Channel.Name}({message.Channel.Id}):{message.Author.Username}:{message.Content}");

		int argPos = 0;
		if (!message.HasStringPrefix(_config["commandPrefix"], ref argPos)
			&& !message.HasMentionPrefix(_discord.CurrentUser, ref argPos)) return;

		var context = new SocketCommandContext(_discord, message);
		await _commands.ExecuteAsync(context, argPos, _provider);
	}
	public async Task CommandExecutedAsync(Discord.Optional<CommandInfo> command, ICommandContext context, IResult result)
	{
		// We can tell the user what went wrong
		if (!string.IsNullOrEmpty(result?.ErrorReason))
		{
			if ((result as ExecuteResult?)?.Exception is Exception e)
			{
				var builder = new EmbedBuilder()
					.WithTitle(e.GetType().Name)
					.WithDescription(e.Message)
					.WithColor(Color.Red);
				
				await context.Channel.SendMessageAsync(embed: builder.Build());
			}
			else
			{
				await context.Channel.SendMessageAsync(result.ErrorReason);
			}
		}

		// ...or even log the result (the method used should fit into
		// your existing log handler)
		if (result.IsSuccess)
		{
			_logger.LogInformation($"Successfully handled command `{command.Value.Name}` ");
		}
		else
		{
			if (command.IsSpecified)
			{
				_logger.LogInformation($"Failed to handle command `{command.Value.Name}`");
			}
			else
			{
				_logger.LogInformation($"Failed to process: `{context.Message}`");
			}
		}
	}
}

public class MetaCommandModule : ModuleBase<SocketCommandContext>
{
	private readonly CommandService _commmands;
	private readonly IConfiguration _config;
	public MetaCommandModule(CommandService commands, IConfiguration config)
	{
		this._commmands = commands;
		this._config = config;
	}

	[Command("help")]
	[Summary("Show this help message")]
	private async Task GetHelp()
	{
		var prefix = _config["commandPrefix"];
		var listBullet = ":sparkles: ";
		
		var builder = new EmbedBuilder()
			.WithTitle("List of commands:")
			.WithDescription(string.Join("\n", _commmands.Commands
				.Select(command => 
					$"{listBullet}**{prefix}{command.Name}{ListCommandParamters(command)}** - {command.Summary}" +
					ListAliases(command)
				)
			))
			.WithColor(Color.Green);

		await ReplyAsync(embed: builder.Build());

		string ListCommandParamters(CommandInfo command)
		{
			return string.Concat(command.Parameters
				.Select(parameter => $" [{(parameter.IsOptional ? "?:" : null)}{parameter.Name}]")
			);
		}
		string ListAliases(CommandInfo command)
		{
			// aliases also contain the command name 
			return command.Aliases.Count > 1
				? "\n alias(es): " + string.Join(", ", command.Aliases.Skip(1).Select(x => $"**{prefix}{x}**"))
				: null;
		}
	}

	[Command("help")]
	[Summary(":construction:todo: Show more information on a specific command")]
	private async Task GetHelp(string commandName)
	{
		throw new NotImplementedException("Soon(tm)");
	}
}
public class LeagueInfoModule : ModuleBase<SocketCommandContext>
{
	private readonly ILogger _logger;
	private readonly ChampionBuild[] _aramBuilds;
	private readonly ChampionBuild[] _summonerRiftBuilds;
	private readonly Dictionary<string, string> _nameMapper;
	private readonly Dictionary<string, string> _summonerRiftLaneMapper;

	public LeagueInfoModule(ILogger<LeagueInfoModule> logger)
	{
		_logger = logger;
		
		_aramBuilds = LoadBuilds("aram.json");
		_summonerRiftBuilds = LoadBuilds("5v5.json");

		_nameMapper = _aramBuilds
			.Select(x => x.Champion)
			.Distinct()
			.ToDictionary(x => string.Concat(x.Where(char.IsLetter)).ToLower());
		_nameMapper.Add("mumdo", "Dr. Mundo");
		_nameMapper.Add("gp", "Gangplank");
		_nameMapper.Add("j4", "Jarvan IV");
		_nameMapper.Add("yi", "Master Yi");
		_nameMapper.Add("mf", "Miss Fortune");
		_nameMapper.Add("tw", "Twisted Fate");

		_summonerRiftLaneMapper = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase)
		{
			["adc"] = "adc",
			["sup"] = "support",
			["supp"] = "support",
			["support"] = "support",
			["mid"] = "mid",
			["jg"] = "jungle",
			["jungle"] = "jungle",
			["top"] = "top",
		};

		ChampionBuild[] LoadBuilds(string file)
		{
			var json = File.ReadAllText(Path.Combine(Util.CurrentQuery.Location, file));
			return JsonConvert.DeserializeObject<ChampionBuild[]>(json);
		}
	}

	[Command("aram")]
	[Summary("Show aram champion build")]
	public async Task GetAramBuild(string championLike)
	{
		var champion = _nameMapper.FirstOrDefault(x => x.Key.StartsWith(championLike.ToLower()));
		if (champion.Key == null)
		{
			throw new ArgumentException($"Could not find champion: {championLike}");
		}
		var build = _aramBuilds.FirstOrDefault(x => x.Champion == champion.Value);
		if (build == null)
		{
			throw new ArgumentException($"Could not find build for `{championLike}`");
		}

		var embed = new EmbedBuilder()
			.WithTitle($"{build.Champion} ({build.Mode})")
			.AddField("Roles", build.Roles)
			.AddField("Summoners", string.Join(", ", build.Summoners.Keys))
			.AddField("Runes", string.Join(", ", build.RunePage.Shards))
			.AddField(build.RunePage.PrimaryPath, string.Join("\n", build.RunePage.Primary.Prepend(build.RunePage.Keystone)), inline: true)
			.AddField(build.RunePage.SecondaryPath, string.Join("\n", build.RunePage.Secondary), inline: true)
			.AddField("Starter Items", string.Join("\n", build.StartingItems.Select(kvp => $"{kvp.Key} @{kvp.Value:P0}")))
			.AddField("Item Build Order", string.Join("\n", build.Items.Select(kvp => $"{kvp.Key} @{kvp.Value:P0}")))
			.AddField("Skill Order", string.Join("\n", build.SkillOrder.Select(kvp => string.Concat(kvp.Value.Select(x => x ?? " "))).Prepend("```").Append("```")))
			.WithThumbnailUrl(build.AvatarUrl)
			.WithColor(Color.Teal)
			.Build();
		
		await RetryHelper.RetryOnException(
			() => ReplyAsync(embed: embed),
			count: 3, delay: TimeSpan.FromSeconds(1), 
			e => _logger.LogError(e, "ReplyAsync failed, retrying")
		);
	}

	[Command("sr")]
	[Alias("rift")]
	[Summary("Show summoner's rift champion build")]
	public async Task GetSummonerRiftBuild(string championLike, string laneLike = null)
	{
		var match = _nameMapper.FirstOrDefault(x => x.Key.StartsWith(championLike.ToLower()));
		if (match.Key == null)
		{
			throw new ArgumentException($"Could not find champion: {championLike}");
		}

		if (laneLike != null && !_summonerRiftLaneMapper.ContainsKey(laneLike))
		{
			throw new ArgumentException($"Invalid lane: `{laneLike}`");
		}

		var build = _summonerRiftBuilds
			.Where(x => x.Champion == match.Value)
			.FirstOrDefault(x => laneLike == null ? x.IsDefaultLane : x.Lane == _summonerRiftLaneMapper[laneLike]);
		if (build == null)
		{
			throw new InvalidOperationException(laneLike == null
				? $"Could not find build for `{championLike}`"
				: $"Could not find build for `{championLike}` as `{laneLike}`");
		}

		var embed = new EmbedBuilder()
			.WithTitle($"{build.Champion} ({build.Mode})")
			.AddField("Lane", build.Lane)
			.AddField("Summoners", string.Join(", ", build.Summoners.Keys))
			.AddField("Runes", string.Join(", ", build.RunePage.Shards))
			.AddField(build.RunePage.PrimaryPath, string.Join("\n", build.RunePage.Primary.Prepend(build.RunePage.Keystone)), inline: true)
			.AddField(build.RunePage.SecondaryPath, string.Join("\n", build.RunePage.Secondary), inline: true)
			.AddField("Starter Items", string.Join("\n", build.StartingItems.Select(kvp => $"{kvp.Key} @{kvp.Value:P0}")))
			.AddField("Item Build Order", string.Join("\n", build.Items.Select(kvp => $"{kvp.Key} @{kvp.Value:P0}")))
			.AddField("Skill Order", string.Join("\n", build.SkillOrder.Select(kvp => string.Concat(kvp.Value.Select(x => x ?? " "))).Prepend("```").Append("```")))
			.WithThumbnailUrl(build.AvatarUrl)
			.WithColor(Color.Teal)
			.Build();

		await RetryHelper.RetryOnException(
			() => ReplyAsync(embed: embed),
			count: 3, delay: TimeSpan.FromSeconds(1),
			e => _logger.LogError(e, "ReplyAsync failed, retrying")
		);
	}
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


public static class RetryHelper
{
	public static Task RetryOnException(Func<Task> func, int count = 3, TimeSpan? delay = default, Action<Exception> beforeRetry = null)
	{
		return RetryOnException<Exception>(func, count, delay, beforeRetry);
	}
	public static async Task RetryOnException<TException>(Func<Task> func, int count = 3, TimeSpan? delay = default, Action<TException> beforeRetry = null) where TException : Exception
	{
		if (count <= 0) throw new ArgumentException(nameof(count));

		for (int i = 1;; i++)
		{
			try
			{
				await func();
				break;
			}
			catch (TException e)
			{
				if (i >= count)
				{
					throw;
				}

				beforeRetry?.Invoke(e);
				await Task.Delay(delay ?? TimeSpan.Zero);
			}
		}
	}
}