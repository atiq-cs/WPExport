namespace WPExportApp {
  using System.Threading.Tasks;
  using Goodbye.WordPress;
  using SharpYaml.Serialization;
  using System.Collections.Generic;
  using System.Linq;

  class WPExportMain {
    /// <summary>
    /// Entry Point
    /// </summary>
    /// <param name="args">CLA</param>
    static async Task Main(string[] args)
    {
      var config = new JsonConfig();
      await config.Load();
      if (config.Options == null)
        throw new System.NullReferenceException();

      var exporter = WordPressExporter.Create(
      postReader: new MysqlPostReader(
        new ConnectionStringBuilder {
          Host = config.Options.Host,
          Database = config.Options.Database,
          Username = config.Options.Username,
          Password = config.Options.Password
          // , TlsVersion = config.Options.TlsVersion
        }),
        contentOutputDirectory: config.Options.ContentOutputDirectory,
        archiveOutputFilePath: config.Options.ArchiveOutputFilePath,
        // And now the delegate...
        @delegate: new CustomExporterDelegate(config.Options.Patterns)
      );

      await exporter.ExportAsync();
    }
  }


  sealed class CustomExporterDelegate : WordPressExporterDelegate
  {
    private Dictionary<string, JsonConfig.ReplacePattern[]> PatternDict;

    public CustomExporterDelegate(Dictionary<string, JsonConfig.ReplacePattern[]> patterns) {
      PatternDict = patterns;
    }

    /// <summary>
    /// Replace weird unicode chars
    /// </summary>
    public string SubstituteCommon(string str) =>
        str.Replace("‘", "'").Replace("’", "'")
            .Replace("“", "\"").Replace("”", "\"");

    /// <summary>
    /// Substitutions for Content and Tag
    /// </summary>
    public string SubstituteInternal(string str, string type) {
      if (! PatternDict.ContainsKey(type)) {
        System.Console.WriteLine($"Json config patterns don't contain {type}!");
        return str;
      }

      var patterns = PatternDict[type];

      if (patterns != null && patterns.Length > 0)
        foreach( var pattern in patterns)
          str = str.Replace(pattern.Needle, pattern.Substitute);
      else
        System.Console.WriteLine("pattern empty!");

      return str;
    }

    /// <summary>Process post content helper</summary>
    private string ProcessContent(string content) {
      content = SubstituteCommon(content)
        .Replace("http://", "https://");
      
      return SubstituteInternal(content, "Content");
    }

    /// <summary>For cleaner URL, post.Name that is tail of URL</summary>
    private string RemoveNonAlphaNumChars(string str)
      => System.Text.RegularExpressions.Regex.Replace(str, "[^A-Za-z0-9-]", "");


    public sealed override string GetOutputPath(
      WordPressExporter exporter, Post post)
    {
      var cleanPostName = RemoveNonAlphaNumChars(
          SubstituteInternal(SubstituteCommon(System.Net.WebUtility.UrlDecode(post.Name)), "Tag")
        );

      System.Console.WriteLine("name: " + System.Net.WebUtility.UrlDecode(post.Name));
      var postDirPath = System.IO.Path.Combine(
        exporter.ContentOutputDirectory,
        $"{post.Published:yyyy}");

      if (!System.IO.Directory.Exists(postDirPath))
        System.IO.Directory.CreateDirectory(postDirPath);

      return System.IO.Path.Combine(postDirPath,
        $"{post.Published:MM-dd}-{cleanPostName}");
    }

    /// <summary>Process post contents</summary>
    public sealed override Post ProcessPost(
      WordPressExporter exporter,
      Post post)
    // Perform the default post processing first by calling base
    => base.ProcessPost(exporter, post) with
    {
      Content = ProcessContent(post.Content)
    };

    /// <summary>Customize YAML front matter</summary>
    public sealed override void PopulatePostYamlFrontMatter(
      WordPressExporter exporter,
      Post post,
      SharpYaml.Serialization.YamlMappingNode rootNode)
    {
      if (!string.IsNullOrEmpty(post.Title))
        rootNode.Add(nameof(Post.Title), SubstituteCommon(post.Title)
          .Trim(new char[] {'"', '\'', ' '}).Replace(":", " -"));

      var finalTags = post.Tags
          .Union(post.Categories)
          .Where(ct => ct != "null" && ct != "uncategorized");

      if (! string.IsNullOrEmpty(post.AuthorName))
          finalTags = finalTags.Union(
              new List<string>() { post.AuthorName }
          );

      if (finalTags.Count() == 0)
          finalTags = new List<string>() { "untagged" };

      rootNode.Add(
          nameof(Post.Tags),
          new YamlSequenceNode(
              finalTags.Select(tag => new YamlScalarNode(SubstituteInternal(
                SubstituteCommon(tag), "Tag").Trim(new char[] {'"', '\'', ' '})))
          )
      );

      base.PopulatePostYamlFrontMatter(exporter, post, rootNode);
    }
  }
}