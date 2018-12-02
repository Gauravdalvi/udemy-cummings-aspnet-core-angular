﻿using AutoMapper;
using DatingApp.API.Data;
using DatingApp.API.Dtos;
using DatingApp.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace DatingApp.API.Controllers
{
    // We are adding the [AllowAnonymous] attribute since we are globally specifying
    // authorization in the the Startup.cs class:
    //services.AddMvc(options => {
    //            var policy = new AuthorizationPolicyBuilder()
    //                // This is going to require authentication globally
    //                .RequireAuthenticatedUser()
    //                .Build();
    //options.Filters.Add(new AuthorizeFilter(policy));
    //        })
    [AllowAnonymous]
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly IAuthRepository _repo;
        private readonly IMapper _mapper;

        public AuthController(IConfiguration config, IAuthRepository repo,
            IMapper mapper)
        {
            _config = config;
            _repo = repo;
            _mapper = mapper;
        }

        // Parameters that we send up from our methods via Http, ASP.NET Core MVC will automatically try to
        // infer the parameters from either the body, from the query string, or from the form.  We can give
        // the RegisterNewUser a hint by adding an attribute [FromBody]
        // for example RegisterNewUser([FromBody]UserForRegisterDto userForRegister).  But since we are using
        // the attribute [ApiController], then this is no longer necessary.  If we remove [ApiController],
        // then we need to use the [FromBody] attribute
        [HttpPost("register")]
        public async Task<IActionResult> RegisterNewUser(UserForRegisterDto userForRegisterDto)
        {
            // If we don't use [ApiController] attribute, then we need to check the ModelState
            // so that the validations specified in the UserForRegisterDto will be triggered
            //if (!ModelState.IsValid)
            //    return BadRequest(ModelState);

            userForRegisterDto.Username = userForRegisterDto.Username.ToLower();

            if (await _repo.UserExists(userForRegisterDto.Username))
            {
                return BadRequest("Username already exists.");
            }

            //_mapper.Map<User>(userForRegisterDto) means 'User' is the destination
            // object to map and 'userForRegisterDto' is the source object
            var userToCreate = _mapper.Map<User>(userForRegisterDto);

            var createdUser = await _repo.Register(userToCreate, userForRegisterDto.Password);

            // This will be our return for the CreatedAtRoute.
            // This is of type UserForDetailedDto so that it will not include sensitive
            // data like password hash/salt and this is the same return type for
            // the method UsersController.GetUser
            var userToReturn = _mapper.Map<UserForDetailedDto>(createdUser);

            // We will return a CreateAtRoute so that we can send back a location
            // header with the request as well as the new resource we have created
            return CreatedAtRoute("GetUser", 
                new { controller = "Users", id = createdUser.Id },
                userToReturn);

            // Just return a 201 Created status
            //return StatusCode(201);
        }

        [HttpPost("login")]
        public async Task<IActionResult> LoginUser(UserForLoginDto userForLogin)
        {
            var userFromRepo = await _repo.Login(userForLogin.Username.ToLower(), 
                userForLogin.Password);

            if (userFromRepo != null)
            {
                // Build-up a token that we are going to return to the user.
                // Our token will contain two bits of information about the user -- Id and UserName
                // We can have additional information to this token since this token can be validated by the server
                // without making a database call.  Once the server gets the token, it take a look inside it
                // and it does not need to go to the database to get the username or user Id
                var claims = new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, userFromRepo.Id.ToString()),
                    new Claim(ClaimTypes.Name, userFromRepo.UserName)
                };

                // We also need a key to sign our token and this part is going to be hashed and it is not readable
                // inside our token itself.
                var key = new SymmetricSecurityKey(Encoding.UTF8
                    .GetBytes(_config.GetSection("SecuritySettings:Token").Value));

                // We also need to generate some signing credentials based on the generated key
                var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha512Signature);

                // We also need to create a security token descriptor which will contain our claims,
                // our expiry date for our tokens and the signing credentials
                var tokenDescriptor = new SecurityTokenDescriptor
                {
                    Subject = new ClaimsIdentity(claims),
                    Expires = DateTime.Now.AddDays(1),
                    SigningCredentials = creds
                };

                // We also need a token handler
                var tokenHandler = new JwtSecurityTokenHandler();

                var token = tokenHandler.CreateToken(tokenDescriptor);

                // We will be using this 'user' object to be return back as an
                // anonymous object to the calling domain.  This is useful like having 
                // a user photo beside the user name
                // in the navigation bar once the user has successfully log-in
                // We are setting the mapping profile for the UserForListDto
                // in the AutoMapperProfiles class so that the main photo url
                // will automatically be mapped.
                var user = _mapper.Map<UserForListDto>(userFromRepo);

                return Ok(new
                {
                    token = tokenHandler.WriteToken(token),
                    user
                });
            }

            // Return a general unauthorized instead if saying the username is correct
            // but the password is wrong so to avoid bruteforcing the password
            return Unauthorized();
        }

    }
}
