using System;
using System.Linq;
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
            if (_context.Users.Any(u => u.Email == email))
                throw new Exception("User already exists.");

            var user = new User
            {
                Email = email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                DisplayName = displayName,
                CreatedAt = DateTime.UtcNow
            };

            _context.Users.Add(user);
            _context.SaveChanges();

            return user;
        }

        public User Login(string email, string password)
        {
            var user = _context.Users.SingleOrDefault(u => u.Email == email);
            if (user == null || !BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
                return null;

            user.LastLoginAt = DateTime.UtcNow;
            _context.SaveChanges();

            return user;
        }
    }
}
