using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Octokit;
using LibGit2Sharp;
using Newtonsoft.Json.Linq;
using Markdig;
using System;
using OpenAI_API;
using System.Text.RegularExpressions;
using System.Linq;
using System.Net.Http.Headers;

namespace AutoContentGenerator
{
    public class BlogPost {
        public string Title { get; set; }
        public string Content { get; set; }
        // public string ImageURL { get; set; }
    }
    public static class GenerateBlogPost
    {
        [FunctionName("GenerateBlogPost")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
        log.LogInformation("C# HTTP trigger function processed a request.");

            // Read GitHub configuration from environment variables
            string gitHubToken = System.Environment.GetEnvironmentVariable("GitHubToken");
            string repoOwner = System.Environment.GetEnvironmentVariable("GitHubRepoOwner");
            string repoName = System.Environment.GetEnvironmentVariable("GitHubRepoName");
            string postsDirectory = System.Environment.GetEnvironmentVariable("GitHubPostsDirectory");

        // log.LogInformation(gitHubToken);
        // log.LogInformation(repoOwner); 
        // log.LogInformation(repoName); 
        // log.LogInformation(postsDirectory);

        log.LogInformation("GitHub configuration from environment variables readed");

            // Initialize GitHub client
            var gitHubClient = new GitHubClient(new Octokit.ProductHeaderValue("AutoContentGenerator"))
            {
                Credentials = new Octokit.Credentials(repoOwner, gitHubToken)
            };

            log.LogInformation("GitHub client Initialized"); 

            // Clone the repository
            string repoUrl = $"https://github.com/{repoOwner}/{repoName}.git";
            string clonePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var libgit2sharpCredentials = new UsernamePasswordCredentials { Username = repoOwner, Password = gitHubToken };
            LibGit2Sharp.Repository.Clone(repoUrl, clonePath, new CloneOptions { CredentialsProvider = (_, __, ___) => libgit2sharpCredentials });

            string postsPath = Path.Combine(clonePath, postsDirectory);
            string[] filePaths = Directory.GetFiles(postsPath);
            string[] fileNames = filePaths.Select(Path.GetFileName).ToArray();
            string filesList = string.Join("\n", fileNames);

            log.LogInformation("Repository cloned"); 

            // Generate the new Markdown file
            var blogPost = await WriteBlogPost(filesList);

            string markdownContent = blogPost.Content;
            string newFilePath = Path.Combine(clonePath, postsDirectory, blogPost.Title + ".md");
            await File.WriteAllTextAsync(newFilePath, markdownContent);

            log.LogInformation("Markdown file generated"); 

            // Download and save the image

            // string imagesPath = Path.Combine(clonePath, "public/images/posts");
            // string imageFileName = $"{blogPost.Title}.png";
            // string savedImagePath = await DownloadAndSaveImage(blogPost.ImageURL, imageFileName, imagesPath);

            string newBranchName;
            // Commit the new file
            using (var repo = new LibGit2Sharp.Repository(clonePath))
            {
                // Create a new branch
                newBranchName = $"blog-post-{DateTime.UtcNow.ToString("yyyyMMddHHmmss")}";
                var branch = repo.CreateBranch(newBranchName);
                Commands.Checkout(repo, branch);

                // Add the new file and commit
                Commands.Stage(repo, newFilePath);
                // Commands.Stage(repo, savedImagePath);
                var author = new LibGit2Sharp.Signature("GPT-Blog-Writer", System.Environment.GetEnvironmentVariable("GitHubEmail"), DateTimeOffset.Now);
                repo.Commit("Add new blog post", author, author);

                // Push the new branch
                var remote = repo.Network.Remotes["origin"];
                var options = new PushOptions { CredentialsProvider = (_, __, ___) => libgit2sharpCredentials };
                repo.Network.Push(remote, $"refs/heads/{newBranchName}", options);
            }

            // Create a pull request
            try
            {
                var pr = new NewPullRequest($"Add new blog post {blogPost.Title}", newBranchName, "master");
                var email = gitHubClient.User.Email;
                var createdPr = await gitHubClient.PullRequest.Create(repoOwner, repoName, pr);

                log.LogInformation("pull request created"); 

                return new OkObjectResult(createdPr.HtmlUrl);
            }
            catch (Octokit.ApiValidationException ex)
            {
                log.LogError($"Validation failed: {ex.Message}");
                return new BadRequestObjectResult(ex.Message);
            }


        }
        public static async Task<BlogPost> WriteBlogPost(string existingPosts)
        {
            string apiKey = System.Environment.GetEnvironmentVariable("OpenAIKey");

            // log.LogInformation(apiKey); 

            string prompt = @$"
Você é redator do meu blog sobre empréstimo consignado para servidores públicos brasileiros chamado Konsi, em konsi.com.br/blog
Este é um blog sobre empréstimo consignado - abordamos todos os aspectos essenciais e tangenciais relacionados ao crédito consignado, educação financeira, etc.
Use markdown ao criar a página.
Não coloque o título em uma tag h1 no início do artigo, pois ele será adicionado separadamente através da página do meu blog.
Vale lembrar que o público-alvo são pessoas de 25 a 64 anos, com nível de escolaridade um pouco superior à média do Brasil, mas com um pouco de dificuldade em se aprofundar em assuntos mais complexos como finanças, por isso os textos além de agregar valor real, devem utilizar recursos como storytelling, exemplos, aplicabilidade prática e citação de outros conteúdos para não perder a atenção dos leitores. Crie consciência e estabilidade na vida financeira dessas pessoas para que elas não precisem se endividar e caso se endividem, que optem por dívidas com a menor taxa de juros. No Brasil, o crédito para pessoas físicas com as menores taxas de juros é o crédito consignado, este deve ser sempre a primeira opção para evitar que os tomadores caiam em dívidas caras, como por exemplo: cheque especial, cartão de crédito, e empréstimos pessoais. Então faz parte do seu papel ensiná-los sobre porquê o empréstimo consignado é o melhor tipo de empréstimo do Brasil. Mas lembre-se de que seu principal objetivo é mantê-los livres de dívidas. Não faça com que pareça um post de vendas. Quando for válido, utilize links Call to Action que levam para a página de download do aplicativo Konsi.
A data de hoje é {DateTime.Now.ToString("yyyy-MM-dd")}.
Include frontmatter on your page in the following format:
---
title: ""<título>""
date: ""{DateTime.Now.ToString("yyyy-MM-dd")}""
author: ""Redação Konsi""
tags:
- ""<tag1>""
- ""<tag2>""
- ""<tag3>""
---
You will receive a list of past topics from the user, write a blog on a brand new topic not listed. Do not repeat a topic already covered. Aim for 2000+ words. Make it SEO optimized. Place links that refer to previous posts throughout the text.
";
            JObject chatRequest = new JObject
            {
                { "model", "gpt-4" },
                { "messages", new JArray { new JObject { { "role", "system" }, { "content", prompt } }, new JObject { { "role", "user" }, { "content", existingPosts } } } }
            };

            string response = await OpenAIService.SendChatRequest(apiKey, chatRequest.ToString());

            string title = null;
            var frontMatterRegex = new Regex(@"---\s*(.*?)---", RegexOptions.Singleline);
            var match = frontMatterRegex.Match(response);

            if (match.Success)
            {
                string frontMatter = match.Groups[1].Value;
                var frontMatterLines = frontMatter.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in frontMatterLines)
                {
                    if (line.StartsWith("title:"))
                    {
                        title = line.Substring("title:".Length).Trim().Trim('"');
                        break;
                    }
                }
            }
            // var imageUrl = await OpenAIService.GenerateImage(apiKey, title);
            var blogPost = new BlogPost();
            blogPost.Title = ToKebabCase(title);
            blogPost.Content = response;
            // blogPost.ImageURL = imageUrl;
            return blogPost;
        }

        public static string ToKebabCase(string title)
        {
            var words = Regex.Split(title, @"\s+")
                .Select(word => Regex.Replace(word.ToLowerInvariant(), @"[^a-z-]", ""));
            return string.Join("-", words.Where(word => !string.IsNullOrWhiteSpace(word)));
        }

        // public static async Task<string> DownloadAndSaveImage(string imageUrl, string fileName, string savePath)
        // {
        //     using var httpClient = new HttpClient();
        //     var response = await httpClient.GetAsync(imageUrl);

        //     if (response.IsSuccessStatusCode)
        //     {
        //         var imageBytes = await response.Content.ReadAsByteArrayAsync();

        //         // Create the directory if it doesn't exist
        //         Directory.CreateDirectory(savePath);

        //         var filePath = Path.Combine(savePath, fileName);
        //         await File.WriteAllBytesAsync(filePath, imageBytes);
        //         return filePath;
        //     }
        //     else
        //     {
        //         throw new HttpRequestException($"Request failed with status code {response.StatusCode}");
        //     }
        // }

    }
}