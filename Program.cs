using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Net.Sockets;
using System.Text.Json;
using System.Web;
using UtilityBelt.Helpers;
using UtilityBelt.Models;
using Microsoft.Extensions.Logging;
using UtilityBelt.Logging;
using System.Composition;
using System.Composition.Hosting;
using System.Reflection;

namespace UtilityBelt
{
  internal class Program
  {
    private static ILogger logger;

    [ImportMany]
    private static IEnumerable<IUtility> utilities { get; set; }

    private static Dictionary<string, IUtility> menu;
    private static Dictionary<string, IUtility> commands;

    private static void Compose()
    {
      var configuration = new ContainerConfiguration()
          .WithAssembly(typeof(Program).GetTypeInfo().Assembly);
      using (var container = configuration.CreateContainer())
      {
        utilities = container.GetExports<IUtility>();
      }
    }

    private static void GenerateMenu()
    {
      menu = new Dictionary<string, IUtility>();
      foreach (var item in utilities)
      {
        menu.Add(item.Name.ToLower(), item);
      }
    }

    private static void GenerateCommands()
    {
      commands = new Dictionary<string, IUtility>();
      foreach (var item in utilities)
      {
        foreach (var c in item.Commands)
        {
          commands.Add(c, item);
        }
      }
    }

    private static void Main(string[] args)
    {
      // Logger
      logger = Logger.InitLogger<Program>();

      logger.LogInformation("Starting Application");
      Console.ForegroundColor = ConsoleColor.Green;
      Console.WriteLine(Properties.Resources.ASCIIart);
      Console.WriteLine("Loading...");

      IServiceProvider services = ServiceProviderBuilder.GetServiceProvider(args);
      IOptions<SecretsModel> options = services.GetRequiredService<IOptions<SecretsModel>>();

      // load all utilities from the current assembly into Utilities list
      // and generate future menu and command list
      Compose();
      GenerateMenu();
      GenerateCommands();

      bool showMenu = true;
      do
      {
        logger.LogInformation("Load Menu Options");
        MenuOptions(options);
        showMenu = RecursiveOptions();
      } while (showMenu);
    }

    #region Choice Handler

    private static bool RecursiveOptions()
    {
      Console.WriteLine("");
      Console.ForegroundColor = ConsoleColor.Green;

      string testUserInput = null;
      do
      {
        Console.Write("Would you like to run another option?: ");
        testUserInput = Console.ReadLine();
        if (int.TryParse(testUserInput, out _)) //If user input was a number...
        {
          logger.LogWarning("Answer could not be translated to a yes/no");
          Console.WriteLine("");
          Console.WriteLine("I am sorry, your answer could not be translated to a yes/no.");
          Console.WriteLine("Please try to reformat your answer.\n");
          testUserInput = null;
        }
      } while (testUserInput == null);

      try
      {
        logger.LogInformation($"Get user Input : {testUserInput}");
        return FromString(testUserInput);
      }
      catch
      {
        logger.LogWarning($"Answer could not be translated to a yes/no : {testUserInput}");
        Console.WriteLine("");
        Console.WriteLine("I am sorry, your answer could not be translated to a yes/no.");
        Console.WriteLine("Please try to reformat your answer.");
        return RecursiveOptions();
      }
    }

    private static void MenuOptions(IOptions<SecretsModel> options)
    {
      logger.LogInformation("Showing Menu Options");

      var itemNumber = 1;
      foreach (var item in menu.Keys)
      {
        Console.WriteLine($"{itemNumber}) {menu[item].Name}");
        itemNumber++;
      }
      Console.WriteLine("0) Exit");

      Console.WriteLine("");

      Console.Write("Your choice:");
      string optionPicked = Console.ReadLine().ToLower();
      logger.LogInformation($"User Choice : {optionPicked}");

      var isNumberPicked = int.TryParse(optionPicked, out int optionNumber);

      IUtility util = null;
      if (isNumberPicked)
      {
        if (optionNumber == 0)
          Environment.Exit(0);

        if (optionNumber > 0 && optionNumber <= menu.Keys.ToList().Count)
        {
          var key = menu.Keys.ToList()[optionNumber - 1];
          util = menu[key];
        }
      }
      else
      {
        if (menu.ContainsKey(optionPicked))
        {
          util = menu[optionPicked];
        }
        else
        {
          if (commands.ContainsKey(optionPicked))
          {
            util = commands[optionPicked];
          }
        }
      }

      if (util != null)
      {
        logger.LogInformation($"User Choice : {util.Name}");
        util.Configure(options);
        util.Run();
      }
      else
      {
        Console.WriteLine("Please make a valid option");
        MenuOptions(options);
      }
    }

    #endregion Choice Handler

    #region Choice Processors


    #region Text Message

    private static void TextMessage(IOptions<SecretsModel> options)
    {
      logger.LogInformation($"User Choice : {nameof(TextMessage)}");
      Console.WriteLine();
      Console.Write("Input number to receive message:");

      string send2Number = Console.ReadLine().ToLower();

      SmtpClient client = new SmtpClient("smtp.gmail.com", 587)
      {
        Credentials = new NetworkCredential(options.Value.Email, options.Value.EmailPassword),
        EnableSsl = true
      };

      MailMessage message = new MailMessage();
      message.From = new MailAddress(options.Value.Email);

      var carrierType = MenuEnum<CarrierType>("Carrier");
      var meta = EnumHelper.GetAttributeOfType<CarrierMetaAttribute>(carrierType);

      message.To.Add(new MailAddress(send2Number + $"@{meta?.Domain}"));
      message.Subject = "This is my subject";
      message.Body = "This is the content";

      try
      {
        client.Send(message);
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("Sent successfully");
      }
      catch
      {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("Text Message failed");
      }

      message.Dispose();
      client.Dispose();
    }

    private static T MenuEnum<T>(string subject)
            where T : struct, Enum
    {
      Console.WriteLine("");
      Console.WriteLine($"Select the {subject}");

      Array values = Enum.GetValues(typeof(T));
      for (int i = 0; i < values.Length; i++)
      {
        var current = values.GetValue(i) as Enum;
        var meta = EnumHelper.GetAttributeOfType<CarrierMetaAttribute>(current);
        string description = meta?.Name ?? Enum.GetName(typeof(T), current);
        Console.WriteLine($"{i + 1}) {description}");
      }

      Console.WriteLine("");

      Console.Write("Your choice:");
      string optionPicked = Console.ReadLine().ToLower();
      logger.LogInformation($"User Choice : {optionPicked}");

      bool isValid = Enum.TryParse(optionPicked, true, out T result);

      if (!isValid || !Enum.IsDefined(typeof(T), result))
      {
        logger.LogWarning($"Invalid User Choice : {optionPicked}");
        Console.WriteLine("Please select a valid option");
        return MenuEnum<T>(subject);
      }

      return result;
    }

    #endregion Text Message

    #region Cat facts

    private static void CatFact()
    {
      logger.LogInformation($"User Choice : {nameof(CatFact)}");
      string content = string.Empty;
      string catUrl = "https://cat-fact.herokuapp.com/facts/random";
      using (var wc = new WebClient())
      {
        content = wc.DownloadString(catUrl);
      }
      CatFactModel catFact = JsonSerializer.Deserialize<CatFactModel>(content);
      Console.WriteLine();
      if (catFact.Status != null && catFact.Status.Verified)
        Console.ForegroundColor = ConsoleColor.Yellow;
      else
        Console.ForegroundColor = ConsoleColor.Red;
      Console.WriteLine(catFact.Text);
      Console.WriteLine();
    }

    #endregion Cat facts

    #region People in space

    private static void Space()
    {
      logger.LogInformation($"User Choice : {nameof(Space)}");
      string content = string.Empty;
      string spacePeopleUrl = "http://api.open-notify.org/astros.json";
      using (var wc = new WebClient())
      {
        content = wc.DownloadString(spacePeopleUrl);
      }
      SpacePersonModel spacePeopleFact = JsonSerializer.Deserialize<SpacePersonModel>(content);
      Console.WriteLine();
      Console.ForegroundColor = ConsoleColor.Yellow;
      Console.WriteLine("There are " + spacePeopleFact.People.Count + " in space right now!");
      foreach (SpacePerson spacePerson in spacePeopleFact.People)
      {
        Console.WriteLine(spacePerson.Name + " is in " + spacePerson.Craft);
      }
      Console.WriteLine();
    }

    #endregion People in space

    #region Country information

    private static void CountryInformation()
    {
      logger.LogInformation($"User Choice : {nameof(CountryInformation)}");
      string content = string.Empty;
      Console.WriteLine("");
      Console.WriteLine("Please enter a country name:");

      string countryName = Console.ReadLine().ToLower();
      string url = $"https://restcountries.eu/rest/v2/name/{countryName}";

      try
      {
        using (var wc = new WebClient())
        {
          content = wc.DownloadString(url);
        }

        var countryInformationResult = JsonSerializer.Deserialize<List<CountryInformation>>(content);

        foreach (var item in countryInformationResult)
        {
          Console.WriteLine("===============================================");
          Console.WriteLine($"Country Name: {item.Name}");
          Console.WriteLine($"Capital: {item.Capital}");
          Console.WriteLine($"Region: {item.Region}");
          Console.WriteLine($"Population: {item.Population.ToString("N1")}");
          Console.WriteLine($"Area: {item.Area.ToString("N1")} km²");
          Console.WriteLine("Currencies");
          foreach (var moneda in item.Currencies)
          {
            Console.WriteLine($"*Code:\t\t{moneda.Code}");
            Console.WriteLine($"*Name:\t\t{moneda.Name}");
            Console.WriteLine($"*Symbol:\t{moneda.Symbol}");
          }
          Console.WriteLine("Languages");
          foreach (var language in item.Languages)
          {
            Console.WriteLine($"* Name:\t\t{language.Name} / {language.NativeName}");
          }
          Console.WriteLine("===============================================");
        }
      }
      catch (WebException)
      {
        logger.LogWarning("Country not found");
        Console.WriteLine("Country not found");
      }
    }

    #endregion Country information

    #region Discord Sender

    private static void DiscordWebhook(IOptions<SecretsModel> options)
    {
      logger.LogInformation($"User Choice : {nameof(DiscordWebhook)}");
      string whook = options.Value.DiscordWebhook;

      if (String.IsNullOrEmpty(whook))
      {
        Console.WriteLine("Whoops! You dont have a webhook defined in your config!"); return;
      }

      Console.Write("Enter the message:");
      string msg = Console.ReadLine();

      WebHookContent cont = new WebHookContent()
      {
        content = msg
      };
      string json = JsonSerializer.Serialize(cont);

      using (var www = new HttpClient())
      {
        var content = new StringContent(json);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var task = www.PostAsync(whook, content);
        task.Wait();
      }
      Console.WriteLine("Message sent!");
    }

    #endregion Discord Sender

    #region Random quote

    private static void RandomQuote()
    {
      logger.LogInformation($"User Choice : {nameof(RandomQuote)}");
      string content = string.Empty;
      string quoteUrl = "https://api.forismatic.com/api/1.0/?method=getQuote&lang=en&format=json";
      using (var wc = new WebClient())
      {
        content = wc.DownloadString(quoteUrl);
      }
      QuoteModel quote = JsonSerializer.Deserialize<QuoteModel>(content);
      Console.WriteLine();
      Console.WriteLine(quote.QuoteText);
      Console.WriteLine($"--{quote.QuoteAuthor}");
      Console.WriteLine();
    }

    #endregion Random quote

    #region Random insult

    private static void RandomInsult()
    {
      logger.LogInformation($"User Choice : {nameof(RandomInsult)}");
      string content = string.Empty;
      string apiUrl = "https://evilinsult.com/generate_insult.php?lang=en&type=json";
      using (var wc = new WebClient())
      {
        content = wc.DownloadString(apiUrl);
      }
      EvilInsultModel insultResponse = JsonSerializer.Deserialize<EvilInsultModel>(content);
      Console.WriteLine();
      Console.WriteLine(HttpUtility.HtmlDecode(insultResponse.Insult));
      Console.WriteLine();
    }

    #endregion Random insult

    #region Who stole the cookie

    private static void CookieAccusation()
    {
      logger.LogInformation($"User Choice : {nameof(CookieAccusation)}");
      string content = string.Empty;
      string suspectUrl = "https://randomuser.me/api/?inc=name&format=json";
      using (var wc = new WebClient())
      {
        content = wc.DownloadString(suspectUrl);
      }
      CookieSuspectModel cookieSuspect = JsonSerializer.Deserialize<CookieSuspectModel>(content);
      string suspectFullName = cookieSuspect.Results[0].Name.First + " " + cookieSuspect.Results[0].Name.Last;
      Console.WriteLine();
      Console.ForegroundColor = ConsoleColor.Yellow;
      Console.WriteLine("");
      Console.WriteLine("Jacques Clouseau: " + suspectFullName + " stole the cookie from the cookie jar.");
      Console.WriteLine("");
      Console.WriteLine(suspectFullName + ": Who, me?");
      Console.WriteLine("");
      Console.WriteLine("Jacques Clouseau: Yes, you!");
      Console.WriteLine("");
      Console.WriteLine(suspectFullName + ": Couldn't be!");
      Console.WriteLine("");
      Console.WriteLine("Jacques Clouseau: Then who?");
      Console.WriteLine();
    }

    #endregion Who stole the cookie

    #region Random taco recipe

    private static void RandomTaco()
    {
      logger.LogInformation($"User Choice : {nameof(RandomTaco)}");
      string content = string.Empty;
      string tacoRandomizerUrl = "http://taco-randomizer.herokuapp.com/random/";
      using (var wc = new WebClient())
      {
        content = wc.DownloadString(tacoRandomizerUrl);
      }

      TacoRecipeModel recipe = JsonSerializer.Deserialize<TacoRecipeModel>(content);
      Console.WriteLine();
      Console.WriteLine(@$"Why not try {recipe.BaseLayer.Name} with {recipe.Mixin.Name},");
      Console.WriteLine(@$"seasoned with {recipe.Seasoning.Name}, topped off with {recipe.Condiment.Name}");
      Console.WriteLine(@$"and wrapped in delicious {recipe.Shell.Name}.");

      Console.WriteLine();
    }

    #endregion Random taco recipe

    #region Covid-19

    private static void Covid19()
    {
      logger.LogInformation($"User Choice : {nameof(Covid19)}");
      while (true)
      {
        Console.WriteLine("");
        Console.WriteLine("Enter the country name to get the information. If you want to see the global information, type \"Global\". For the list of all countries type \"List\". To exit Covid-19 Statistics type \"Exit\": ");
        string userInput = Console.ReadLine();
        Console.WriteLine("");

        if (userInput == "Exit")
          return;

        string content = string.Empty;
        using (var wc = new WebClient())
        {
          content = wc.DownloadString("https://api.covid19api.com/summary");
        }
        CovidRoot summary = JsonSerializer.Deserialize<CovidRoot>(content);

        if (userInput.StartsWith("List", StringComparison.InvariantCultureIgnoreCase))
        {
          userInput = userInput.Substring(4).TrimStart();
          Console.WriteLine("Found countries: ");
          foreach (var countryJson in summary.Countries)
          {
            if (countryJson.Country.StartsWith(userInput, StringComparison.InvariantCultureIgnoreCase))
              Console.WriteLine(countryJson.Country);
          }
        }
        else if (userInput.Equals("Global", StringComparison.InvariantCultureIgnoreCase))
        {
          ShowCovidInfo("Global", summary.Global.NewConfirmed, summary.Global.TotalConfirmed, summary.Global.NewDeaths, summary.Global.TotalDeaths, summary.Global.NewRecovered, summary.Global.TotalRecovered);
        }
        else
        {
          bool countryExists = false;

          foreach (var countryJson in summary.Countries)
          {
            if (userInput.Equals(countryJson.Country, StringComparison.InvariantCultureIgnoreCase))
            {
              ShowCovidInfo(countryJson.Country, countryJson.NewConfirmed, countryJson.TotalConfirmed, countryJson.NewDeaths, countryJson.TotalDeaths, countryJson.NewRecovered, countryJson.TotalRecovered);
              countryExists = true;
            }
          }

          if (countryExists == false)
            Console.WriteLine("Country does not exist. Type \"List\" to see the list of available countries.");
        }
      }
    }

    private static void ShowCovidInfo(string countryName, long newConfirmed, long totalConfirmed, long newDeaths, long totalDeaths, long newRecovered, long totalRecovered)
    {
      Console.WriteLine("Statistics: " + countryName);
      Console.ForegroundColor = ConsoleColor.Cyan;
      Console.WriteLine("New Confirmed: " + newConfirmed);
      Console.WriteLine("Total Confirmed: " + totalConfirmed);
      Console.ForegroundColor = ConsoleColor.Red;
      Console.WriteLine("New Deaths: " + newDeaths);
      Console.WriteLine("Total Deaths: " + totalDeaths);
      Console.ForegroundColor = ConsoleColor.Green;
      Console.WriteLine("New Recovered: " + newRecovered);
      Console.WriteLine("Total Recovered: " + totalRecovered);
    }

    #endregion Covid-19

    #region GeekJokes

    private static void GeekJokes()
    {
      logger.LogInformation($"User Choice : {nameof(GeekJokes)}");
      IWebProxy defaultWebProxy = WebRequest.DefaultWebProxy;
      defaultWebProxy.Credentials = CredentialCache.DefaultCredentials;

      string content = string.Empty;
      string geekJokeUrl = "https://geek-jokes.sameerkumar.website/api?format=json";
      using (var wc = new WebClient() { Proxy = defaultWebProxy })
      {
        content = wc.DownloadString(geekJokeUrl);
      }

      GeekJokeModel joke = JsonSerializer.Deserialize<GeekJokeModel>(content);
      Console.WriteLine();
      Console.WriteLine(@$"The Geek says -- {joke.Joke}");

      Console.WriteLine();
    }

    #endregion GeekJokes

    #region NumberFact

    private static void NumberFact()
    {
      logger.LogInformation($"User Choice : {nameof(NumberFact)}");
      string content = string.Empty;
      int numberEntered;

      Console.WriteLine("Please enter an integer number");
      String userInput = Console.ReadLine();

      if (int.TryParse(userInput, out numberEntered))
      {
        string numberFactURL = $"http://numbersapi.com/{userInput}?format=json";
        using (var wc = new WebClient())
        {
          content = wc.DownloadString(numberFactURL);
        }

        Console.WriteLine("Random fact for number " + userInput + ":");
        Console.WriteLine(content);
      }
      else
      {
        Console.WriteLine("Input was not an integer");
      }
    }

    #endregion NumberFact

    #region Random Advice

    private static void RandomAdvice()
    {
      logger.LogInformation($"User Choice : {nameof(RandomAdvice)}");
      string content = string.Empty;
      string adviceUrl = "https://api.adviceslip.com/advice";
      using (var wc = new WebClient())
      {
        content = wc.DownloadString(adviceUrl);
      }

      AdviceModel randomAdvice = JsonSerializer.Deserialize<AdviceModel>(content);
      Console.WriteLine("Here's your advice:");
      Console.ForegroundColor = ConsoleColor.Cyan;
      Console.WriteLine(randomAdvice.Slip.Advice);
      Console.WriteLine();
    }

    #endregion Random Advice

    #region Space Station Location

    private static void SpaceStationLocation()
    {
      logger.LogInformation($"User Choice : {nameof(SpaceStationLocation)}");
      string content = string.Empty;
      string adviceUrl = "http://api.open-notify.org/iss-now.json";
      using (var wc = new WebClient())
      {
        content = wc.DownloadString(adviceUrl);
      }

      SpaceStationLocation spaceStation = JsonSerializer.Deserialize<SpaceStationLocation>(content);
      Console.WriteLine("Woah! The International Space Station is currently at:");
      Console.ForegroundColor = ConsoleColor.Cyan;
      Console.WriteLine($@"{spaceStation.Location.Latitude} Latitude and {spaceStation.Location.Longitude} Longitude");
      Console.Write("That's so cool!");
      Console.WriteLine();
    }

    #endregion Space Station Location

    #region Random quote

    private static void RandomQuoteGarden()
    {
      logger.LogInformation($"User Choice : {nameof(RandomQuoteGarden)}");
      IWebProxy defaultWebProxy = WebRequest.DefaultWebProxy;
      defaultWebProxy.Credentials = CredentialCache.DefaultCredentials;

      string content = string.Empty;
      string randomQuoteUrl = "https://quote-garden.herokuapp.com/api/v2/quotes/random";
      using (var wc = new WebClient() { Proxy = defaultWebProxy })
      {
        content = wc.DownloadString(randomQuoteUrl);
      }

      RandomQuoteModel quote = JsonSerializer.Deserialize<RandomQuoteModel>(content);
      Console.WriteLine();
      Console.WriteLine(@$"Quote -- {quote.Quote.Text}");
      Console.WriteLine(@$"From -- {quote.Quote.Author}");

      Console.WriteLine();
    }

    #endregion Random quote

    #region Console Calculator

    private static void ConsoleCalculator()
    {
      logger.LogInformation($"User Choice : {nameof(ConsoleCalculator)}");
      // Declare variables and then initialize to zero.
      int num1 = 0; int num2 = 0;

      // Display title as the C# console calculator app.
      Console.WriteLine("Console Calculator in C#\r");
      Console.WriteLine("------------------------\n");

      // Ask the user to type the first number.
      Console.WriteLine("Type a number, and then press Enter");
      num1 = Convert.ToInt32(Console.ReadLine());

      // Ask the user to type the second number.
      Console.WriteLine("Type another number, and then press Enter");
      num2 = Convert.ToInt32(Console.ReadLine());

      // Ask the user to choose an option.
      Console.WriteLine("Choose an option from the following list:");
      Console.WriteLine("\ta - Add");
      Console.WriteLine("\ts - Subtract");
      Console.WriteLine("\tm - Multiply");
      Console.WriteLine("\td - Divide");
      Console.Write("Your option? ");

      // Use a switch statement to do the math.
      switch (Console.ReadLine())
      {
        case "a":
          Console.WriteLine($"Your result: {num1} + {num2} = " + (num1 + num2));
          break;

        case "s":
          Console.WriteLine($"Your result: {num1} - {num2} = " + (num1 - num2));
          break;

        case "m":
          Console.WriteLine($"Your result: {num1} * {num2} = " + (num1 * num2));
          break;

        case "d":
          Console.WriteLine($"Your result: {num1} / {num2} = " + (num1 / num2));
          break;
      }
      // Wait for the user to respond before closing.
      Console.Write("Press any key to close the Calculator console app...");
      Console.ReadKey();
    }

    #endregion Console Calculator

    #region Gender from name

    private static void GenderFromName()
    {
      logger.LogInformation($"User Choice : {nameof(GenderFromName)}");
      Console.Write("Type the name, and then press Enter: ");
      string enteredName = Console.ReadLine().ToLower();

      if (string.IsNullOrEmpty(enteredName))
      {
        Console.WriteLine("No name provided!");
        Console.WriteLine();
        return;
      }

      IWebProxy defaultWebProxy = WebRequest.DefaultWebProxy;
      defaultWebProxy.Credentials = CredentialCache.DefaultCredentials;

      string content = string.Empty;
      string genderizatorUrl = $"https://api.genderize.io?name={enteredName}";
      using (var wc = new WebClient() { Proxy = defaultWebProxy })
      {
        content = wc.DownloadString(genderizatorUrl);
      }

      GenderizatorModel genderResult = JsonSerializer.Deserialize<GenderizatorModel>(content);
      Console.WriteLine();
      Console.WriteLine(@$"The gender is {genderResult.Gender} with a probability of {genderResult.Probability}");

      Console.WriteLine();
    }

    #endregion Gender from name

    #region Dad Joke

    private static void DadJoke()
    {
      logger.LogInformation($"User Choice : {nameof(DadJoke)}");
      WebRequest request = WebRequest.Create("https://icanhazdadjoke.com");

      request.Headers.Add("User-Agent", "GitHub library https://github.com/aherd2985/UtilityBelt");
      request.Headers.Add("Accept", "application/json");

      // Get the response.
      WebResponse response = request.GetResponse();

      var status = ((HttpWebResponse)response).StatusCode;

      if (status != HttpStatusCode.OK)
      {
        Console.WriteLine(@"There was an error retrieving the joke. Please try again later.");
      }
      else
      {
        using Stream dataStream = response.GetResponseStream();

        if (dataStream != null)
        {
          StreamReader reader = new StreamReader(dataStream);
          // Read the content.
          string responseFromServer = reader.ReadToEnd();
          // Display the content.
          DadJokeModel dadJoke = JsonSerializer.Deserialize<DadJokeModel>(responseFromServer);
          Console.WriteLine(@"Random Dad Joke:");
          Console.WriteLine(dadJoke.Joke);
        }
        else
        {
          Console.WriteLine(@"The joke data is null. This is probably bad.");
        }
      }

      // Close the response.
      response.Close();
    }

    #endregion Dad Joke

    #region Random pada fact

    private static void RandomPandaFact()
    {
      logger.LogInformation($"User Choice : {nameof(RandomPandaFact)}");
      string content;
      string url = "https://some-random-api.ml/facts/panda";
      using (var wc = new WebClient())
      {
        content = wc.DownloadString(url);
      }

      PandaFactModel pandaFact = JsonSerializer.Deserialize<PandaFactModel>(content);
      Console.WriteLine();
      Console.ForegroundColor = ConsoleColor.Yellow;
      Console.WriteLine(pandaFact.Fact);
      Console.WriteLine();
    }

    #endregion Random pada fact

    #region Random fox fact

    private static void RandomFoxFact()
    {
      logger.LogInformation($"User Choice : {nameof(RandomFoxFact)}");
      string content;
      string url = "https://some-random-api.ml/facts/fox";
      using (var wc = new WebClient())
      {
        content = wc.DownloadString(url);
      }

      PandaFactModel foxFact = JsonSerializer.Deserialize<PandaFactModel>(content);
      Console.WriteLine();
      Console.ForegroundColor = ConsoleColor.Yellow;
      Console.WriteLine(foxFact.Fact);
      Console.WriteLine();
    }

    #endregion Random fox fact

    #region HostToIp

    private static void HostToIp()
    {
      while (true)
      {
        logger.LogInformation($"User Choice : {nameof(HostToIp)}");
        Console.Write("Please enter a hostname: ");
        var hostname = Console.ReadLine();
        if (string.IsNullOrEmpty(hostname) && Uri.CheckHostName(hostname) != UriHostNameType.Unknown)
        {
          continue;
        }

        Console.WriteLine($"Hostname: {hostname}");
        try
        {
          var ipAddresses = Dns.GetHostAddresses(hostname);
          for (var i = 0; i < ipAddresses.Length; i++)
          {
            Console.WriteLine($"IP[{i + 1}]: {ipAddresses[i]}");
          }

          break;
        }
        catch (SocketException e)
        {
          Console.WriteLine("Invalid hostname - " + e.Message);
          break;
        }
        catch (Exception e)
        {
          Console.WriteLine("An error occurred: " + e.Message);
        }
      }
    }

    #endregion HostToIp

    #region Ghost Game

    private static void GhostGame()
    {
      logger.LogInformation($"User Choice : {nameof(GhostGame)}");
      Random rNumber = new Random();
      int score = 0;
      int randomNum = rNumber.Next(1, 4);
      int numberInput;
      bool validDoor = false;
      bool validNumber;

      while (true)
      {
        Console.WriteLine("\nThere are three doors ahead! \n");
        Console.WriteLine("A ghost is behind one of them :O! \n");
        Console.WriteLine("Which door do you open?!?! \n");
        Console.WriteLine("1, 2 or 3?\n");

        do
        {
          validNumber = int.TryParse(Console.ReadLine(), out numberInput);

          if (Enumerable.Range(1, 3).Contains(numberInput) && validNumber)//If user selected a valid door ...
          {
            validDoor = true;
          }
          else
          {
            Console.WriteLine("");
            if (validNumber)
            {
              Console.WriteLine("\nThere are only three doors ahead. \n");
            }
            else
            {
              Console.WriteLine("Input was not an integer.");
            }
            Console.WriteLine("Please select door 1, 2, or 3.\n");
            validDoor = false;
          }
        } while (validDoor == false);

        if (numberInput == randomNum)
        {
          Console.WriteLine("\nBoo!! GHOST!");
          break;
        }
        else
        {
          Console.WriteLine("\nNo ghost, Phew!");
          Console.WriteLine("\nPress any key to enter the next room!");
          Console.ReadKey();
          randomNum = rNumber.Next(1, 4);
          score += 1;
        }
      }
      Console.WriteLine("Run!!!!!");
      Console.WriteLine("\nGame Over! Score: " + score);
    }

    #endregion Ghost Game

    #region Breaking Bad Quotes

    private static void BreakingBadQuotes()
    {
      logger.LogInformation($"User Choice : {nameof(BreakingBadQuotes)}");
      IWebProxy defaultWebProxy = WebRequest.DefaultWebProxy;
      defaultWebProxy.Credentials = CredentialCache.DefaultCredentials;

      string content = string.Empty;
      string breakingBadQuoteUrl = $"https://breaking-bad-quotes.herokuapp.com/v1/quotes";
      using (var wc = new WebClient() { Proxy = defaultWebProxy })
      {
        content = wc.DownloadString(breakingBadQuoteUrl);
      }

      List<BreakingBadQuoteModel> quote = JsonSerializer.Deserialize<List<BreakingBadQuoteModel>>(content);
      Console.WriteLine();
      Console.WriteLine(@$"Quote -- {quote.FirstOrDefault().Quote}");
      Console.WriteLine(@$"From -- {quote.FirstOrDefault().Author}");

      Console.WriteLine();
    }

    #endregion Breaking Bad Quotes

    #region Digital Ocean

    private static void DigitalOceanStatus()
    {
      logger.LogInformation($"User Choice : {nameof(DigitalOceanStatus)}");
      IWebProxy defaultWebProxy = WebRequest.DefaultWebProxy;
      defaultWebProxy.Credentials = CredentialCache.DefaultCredentials;

      string content = string.Empty;
      string digitalOceanStatusUrl = $"https://s2k7tnzlhrpw.statuspage.io/api/v2/status.json";
      using (var wc = new WebClient() { Proxy = defaultWebProxy })
      {
        content = wc.DownloadString(digitalOceanStatusUrl);
      }

      DigitalOceanStatusModel digitalOceanStatus = JsonSerializer.Deserialize<DigitalOceanStatusModel>(content);
      Console.WriteLine();
      Console.WriteLine(@$"Indicator -- {digitalOceanStatus.Status.Indicator}");
      Console.WriteLine(@$"Description -- {digitalOceanStatus.Status.Description}");

      Console.WriteLine();
    }

    #endregion Digital Ocean

    #region Random User Generator

    private static void RandomUserGenerator()
    {
      logger.LogInformation($"User Choice : {nameof(RandomUserGenerator)}");
      string content = string.Empty;
      string randomUserUrl = @"https://randomuser.me/api";
      using (var wc = new WebClient())
      {
        content = wc.DownloadString(randomUserUrl);
      }

      RandomUser randomUser = JsonSerializer.Deserialize<RandomUser>(content);
      Console.WriteLine();
      Console.ForegroundColor = ConsoleColor.Cyan;

      int cnt = 1;

      foreach (var user in randomUser.results)
      {
        Console.WriteLine($"User Generated Nbr: {cnt}");
        Console.WriteLine($"Gender: {user.Gender}");
        Console.WriteLine($"Name: {user.Name.First} {user.Name.Last}");
        Console.WriteLine($"Email: {user.Email}");
        Console.WriteLine($"Phone: {user.Phone}");
        Console.WriteLine("");
      }

      Console.WriteLine();
    }

    #endregion Random User Generator

    #endregion Choice Processors

    #region Utility

    internal class WebHookContent
    {
      public string content { get; set; }
    }

    private enum BooleanAliases
    {
      YES = 1,
      AYE = 1,
      COOL = 1,
      TRUE = 1,
      Y = 1,
      YEAH = 1,
      NAW = 0,
      NO = 0,
      FALSE = 0,
      N = 0
    }

    private static bool FromString(string str)
    {
      return Convert.ToBoolean(Enum.Parse(typeof(BooleanAliases), str.ToUpper()));
    }

    #endregion Utility
  }
}