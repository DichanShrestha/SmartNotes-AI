using System;
using System.Linq;
using System.Security.Cryptography;
using SmartNotesAI.Core.DTOs;
using SmartNotesAI.Core.Models;
using SmartNotesAI.Data;
using BCrypt.Net;

namespace SmartNotesAI.Services
{
    public class AuthService
    {
        private readonly SmartNotesDbContext _context;

        public AuthService(SmartNotesDbContext context)
        {
            _context = context;
        }

        public User Register(string email, string password, string displayName)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Email and password are required.");

            if (_context.Users.Any(u => u.Email.ToLower() == email.ToLower()))
                throw new InvalidOperationException("User with this email already exists.");

            var user = new User
            {
                Email = email.Trim(),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? email.Split('@')[0] : displayName.Trim(),
                CreatedAt = DateTime.UtcNow
            };

            _context.Users.Add(user);
            _context.SaveChanges();

            return user;
        }

        public User Login(string email, string password)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                return null;

            var user = _context.Users.SingleOrDefault(u => u.Email.ToLower() == email.ToLower());
            if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
                return null;

            user.LastLoginAt = DateTime.UtcNow;
            user.RefreshToken = GenerateRefreshTokenString();
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(14);
            _context.SaveChanges();

            return user;
        }

        public User RefreshToken(int userId, string refreshToken)
        {
            var user = _context.Users.Find(userId);
            if (user == null || user.RefreshToken != refreshToken || user.RefreshTokenExpiryTime <= DateTime.UtcNow)
                return null;

            user.RefreshToken = GenerateRefreshTokenString();
            user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(14);
            _context.SaveChanges();

            return user;
        }

        public void RevokeRefreshToken(int userId)
        {
            var user = _context.Users.Find(userId);
            if (user != null)
            {
                user.RefreshToken = null;
                user.RefreshTokenExpiryTime = null;
                _context.SaveChanges();
            }
        }

        public static string GenerateRefreshTokenString()
        {
            var randomNumber = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomNumber);
                return Convert.ToBase64String(randomNumber);
            }
        }
    }
}
