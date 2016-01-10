private const string MeetupApiKey = "XXXX";
private const string MeetupSecret = "XXXX";

private static string[] priorityMeetups = new[] { "Origin-Code-Academy-Meetup", "San-Diego-NET-Users-Group", "sandiegojs", "Coding-Interview-Practice-San-Diego" };
private static string[] optionalMeetups = new[] { "SanDiegoPHP", "sdruby", "sandiego-ember", "pythonsd", "sdiosdevelopers", "San-Diego-Young-Entrepreneur-Network", "Geek-Girl-San-Diego", "San-Diego-ReactJS-Group", "San-Diego-Cybersecurity-Immersion-Group" };

static async Task postMeetupsToSlack()
{
	// File path for storign meetup OAuth token
	string tokenPath = Environment.CurrentDirectory + "/meetup.token";
	string value = "", secret = "";

	// MeetupServiceProvider is a nuget package that communicates with Meetup REST API
	var meetupServiceProvider = new MeetupServiceProvider(MeetupApiKey, MeetupSecret);

	// Try to get the token
	if (File.Exists(tokenPath))
	{
		// If we already saved it, grab it from the file system
		var file = File.ReadAllText(tokenPath);

		value = file.Split(',')[0];
		secret = file.Split(',')[1];
	}
	else
	{
		// No token in the file system... Do the OAuth dance!
		Console.Write("Getting request token...");
		var oauthToken = await meetupServiceProvider.OAuthOperations.FetchRequestTokenAsync("oob", null);
		Console.WriteLine("Done");

		var authenticateUrl = meetupServiceProvider.OAuthOperations.BuildAuthorizeUrl(oauthToken.Value, null);
		Console.WriteLine("Redirect user for authentication: " + authenticateUrl);
		Process.Start(authenticateUrl);
		Console.WriteLine("Enter PIN Code from Meetup authorization page:");
		var pinCode = Console.ReadLine();

		Console.Write("Getting access token...");
		var requestToken = new AuthorizedRequestToken(oauthToken, pinCode);
		var oauthAccessToken = await meetupServiceProvider.OAuthOperations.ExchangeForAccessTokenAsync(requestToken, null);
		Console.WriteLine("Done");

	   // Save token for later
		File.WriteAllText(tokenPath, oauthAccessToken.Value + "," + oauthAccessToken.Secret);
	}

	// Build up a string in Slack Messaging Format
	var sb = new StringBuilder();
	sb.AppendLine("\r\nHello friends! Here's what this weeks meetups look like.");
	sb.AppendLine();

	int meetupCount = 0;

	// Get information about the two categories of meetups we define at Origin
	meetupCount += await getGroups(meetupServiceProvider, sb, value, secret, "Recommended", priorityMeetups);
	meetupCount += await getGroups(meetupServiceProvider, sb, value, secret, "Optional", optionalMeetups);

	if (meetupCount > 0)
	{
		// If we have meetups, send'em to slack!
		await Slack.SendMeetupNotification(sb.ToString());
	}

	Console.ReadLine();
}

static async Task<int> getGroups(MeetupServiceProvider meetupServiceProvider, StringBuilder sb, string value, string secret, string title, string[] meetups)
{
	sb.AppendLine();

	int meetupCount = 0;

	var meetup = meetupServiceProvider.GetApi(value, secret);

	var groupResult = await meetup.RestOperations.GetForObjectAsync<string>($"https://api.meetup.com/2/groups?group_urlname={string.Join(",", meetups)}");

	var oGroups = JObject.Parse(groupResult)["results"];

	foreach (var oGroup in oGroups)
	{
		double startDate = DateTime.Now.Date.ToUnix() * 1000;
		double endDate = DateTime.Now.Date.AddDays(7).AddSeconds(-1).ToUnix() * 1000;

		string eventResult = await meetup.RestOperations.GetForObjectAsync<string>($"https://api.meetup.com/2/events?group_urlname={oGroup["urlname"]}&time={startDate},{endDate}");

		var oEvents = JObject.Parse(eventResult)["results"];

		if (oEvents.Count() > 0)
		{
			int eventCount = 0;
			meetupCount++;
			sb.AppendLine($"*{oGroup["name"]} ({title})*");
			foreach (var oEvent in oEvents)
			{
				eventCount++;
				long time = (long)oEvent["time"];
				DateTime dateTime = time.ToDateTime();

				sb.AppendLine($"```\r\n" + oEvent["name"].ToString() + "");
				sb.AppendLine(dateTime.ToString("dddd MMMM dd, hh:mm tt"));
				sb.AppendLine($"{oEvent["yes_rsvp_count"]} people going");

				sb.AppendLine("```");
			}
			sb.AppendLine();
		}
	}

	sb.AppendLine();
	sb.AppendLine();

	return meetupCount;
}