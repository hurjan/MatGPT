﻿using MatGPT.Data;
using MatGPT.Models;
using MatGPT.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ValueGeneration.Internal;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using OpenAI_API;
using OpenAI_API.Images;

namespace MatGPT.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class RecipeController : Controller
    {
        private readonly ApplicationContext _context;
        private readonly OpenAIAPI _api;

        public RecipeController(ApplicationContext dbContext, OpenAIAPI api)
        {
            _context = dbContext;
            _api = api;
        }
        //This endpoint generates a recipe. We input ingredients, tools, food preferences etc
        [HttpPost("GenerateRecipe")]
        public async Task<IActionResult> GenerateRecipeAsync(string query, int userId, int minTime, int maxTime, bool chooseTimer, int servings, bool choosePreferences)
        {
            var chat = _api.Chat.CreateConversation();
            chat.Model = OpenAI_API.Models.Model.ChatGPTTurbo;
            chat.RequestParameters.Temperature = 0;

            chat.AppendSystemMessage("You will generate recipes ONLY based on the ingredients provided to you. Do not add things that are not specified as available. Only append title, ingredients, how to make the recipe and state estimated cookingtime - without extra sentences. Answer in English. Return Json in these fields: Title, instructions, ingredients as String and cookingtime as Int.");

            //Filter: Will ensure that generated recipe will use these available tools
            var kitchenSupplies = await _context.KitchenSupply
            .Where(ks => ks.UserId == userId)
            .Select(ks => ks.KitchenSupplyName)
            .ToListAsync();

            string kSUserInput = $"I have these tools available for cooking: {string.Join(", ", kitchenSupplies)}";

            //Filter: Will ensure that generated recipe will use these available ingredients
            var pantryIngredients = await _context.Ingredients
            .Where(fi => fi.UserId == userId)
            .Select(fi => fi.IngredientName)
            .ToListAsync();

            string pFIUserInput = $"I have these ingredients in my usual pantry: {string.Join(", ", pantryIngredients)}";

            //Filter: Tells AI to generate recipe according to time input
            if (chooseTimer)
            {
                string cTUserInput = $"I want a recipe with cooking time between {minTime}-{maxTime} minutes.";
                chat.AppendUserInput(cTUserInput);
            }

            string sUserInput = $"I want {servings} servings";

            //Filter: Will ensure that generated recipe adjusts according to diets/allergies
            if (choosePreferences)
            {
                var foodPreference = await _context.FoodPreferences
                .Where(fp => fp.UserId == userId)
                .Select(fp => fp.FoodPreferenceName)
                .ToListAsync();

                string fPUserInput = $"I want a recipe that takes these allergies or diets into consideration: {string.Join(", ", foodPreference)}";
            }

            chat.AppendUserInput(kSUserInput);

            chat.AppendUserInput(pFIUserInput);


            chat.AppendUserInput(query);


            var answer = await chat.GetResponseFromChatbotAsync();


            //Json-answer from AI
            string jsonResponse = answer;

            //Answer is turned into dynamic object - not needing to know its exact structure
            var responseObject = JsonConvert.DeserializeObject<dynamic>(jsonResponse);

            //Shows result in console for debugging
            Console.WriteLine(responseObject.ToString());

            //Converting response object into string, assign to recipeJson. Preparation for deseralisation into strong typing object
            var recipeJson = responseObject.ToString();

            // Deserialize Json-recipe information into object of RecipeViewModel
            var recipe = JsonConvert.DeserializeObject<RecipeViewModel>(recipeJson);


            //string imageUrl = await GenerateImageByRecipeTitle(recipe.Title, _api);

            //Save the recipe to database Temporarily
            await _context.Recipes.AddAsync(new Recipe
            {
                Title = recipe.Title,
                Instructions = recipe.Instructions,
                Ingredients = recipe.Ingredients,
                CookingTime = recipe.CookingTime,
                UserId = userId
            });

            await _context.SaveChangesAsync();

            // Combine the recipe and image URL into a single object
            var result = new { Recipe = recipe};

            return Ok(result);
        }

        // Image generation Method

        //static async Task<string> GenerateImageByRecipeTitle(string recipeTitle, OpenAIAPI api)
        //{
        //    var result = await api.ImageGenerations.CreateImageAsync(new ImageGenerationRequest($"Food plating image of: {recipeTitle}. Image should be appealing, like an advertisement image.", OpenAI_API.Models.Model.DALLE3));

        //    return result.Data[0].Url;
        //}


        // This endpoint NEEDS to be called after the other ones.
        // The other endpoints will auto save recipes in "temporary storage" and then this decides if the recipe should be deleted
        // or permanently saved to a user.
        [HttpPost("SaveRecipe")]
        public async Task<IActionResult> SaveRecipeAsync(string recipeName, bool saveRecipe)
        {
            // Retrieve user's ID from session
            string userId = HttpContext.Session.GetString("UserId");

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User not authenticated");
            }

            if (saveRecipe)
            {
                if (string.IsNullOrEmpty(recipeName))
                {
                    return BadRequest("Recipe name cannot be empty.");
                }

                // Find the last saved recipe by sorting by userid and latest recipe
                var lastRecipe = await _context.Recipes
                    .Where(r => r.UserId == int.Parse(userId))
                    .OrderByDescending(r => r.RecipeId)
                    .FirstOrDefaultAsync();

                if (lastRecipe == null)
                {
                    return NotFound("No recipe to save.");
                }

                lastRecipe.Title = recipeName;

                try
                {
                    await _context.SaveChangesAsync();
                    return Ok($"Saved the recipe as {recipeName}");
                }
                catch (DbUpdateException ex)
                {
                    return StatusCode(500, "An error occurred while updating the recipe. Please try again later.");
                }
            }
            else
            {
                var lastRecipe = await _context.Recipes
                    .Where(r => r.UserId == int.Parse(userId))
                    .OrderByDescending(r => r.RecipeId)
                    .FirstOrDefaultAsync();

                _context.Recipes.Remove(lastRecipe);
                await _context.SaveChangesAsync();
                return Ok("Recipe not saved, deleted from db.");
            }

        }

        //EndPoint that will be used on the page that will allow user to choose available kitchen tools
        [HttpPost("KitchenSupply")]
        public async Task<IActionResult> AddOrRemoveKitchenSupplyAsync(int  userId, string kitchenSupplyName)
        {
            User? user = await _context.Users
                .Include(u => u.KitchenSupplies)
                .FirstOrDefaultAsync(u => u.UserId == userId);

            if (user == null)
            {
                return NotFound("User not found");
            }

            var existingKitchenSupply = user.KitchenSupplies
                .FirstOrDefault(ks => ks.KitchenSupplyName == kitchenSupplyName);

            //If kitchen supply does not exist for user, it will be added
            if (existingKitchenSupply == null)
            {
                user.KitchenSupplies.Add(new KitchenSupply { KitchenSupplyName = kitchenSupplyName});
                await _context.SaveChangesAsync();
                return Ok();
            }
            //If kitchen tool already exists - it will be removed
            else
            {
                user.KitchenSupplies.Remove(existingKitchenSupply);
                await _context.SaveChangesAsync();
                return Ok();
            }
        }


        //Endpoint that will be used on the pages that will allow user to choose diet and allergies
        [HttpPost("FoodPreference")]
        public async Task<IActionResult> AddOrRemoveFoodPreferenceAsync(int userId, string foodPreferenceName)
        {
           User? user = await _context.Users
                .Include(u => u.FoodPreferences)
                .FirstOrDefaultAsync(u => u.UserId == userId);

            if (user == null)
            {
                return NotFound("User not found");
            }

            var existingFoodPreference = user.FoodPreferences
                .FirstOrDefault(fp => fp.FoodPreferenceName == foodPreferenceName);

            //If food preference does not exist for user, it will be added
            if (existingFoodPreference == null)
            {
                user.FoodPreferences.Add(new FoodPreference { FoodPreferenceName = foodPreferenceName });
                await _context.SaveChangesAsync();
                return Ok();
            }

            //If food preference already exists - it will be removed
            else
            {
                user.FoodPreferences.Remove(existingFoodPreference);
                await _context.SaveChangesAsync();
                return Ok();
            }
        }

    }
}
