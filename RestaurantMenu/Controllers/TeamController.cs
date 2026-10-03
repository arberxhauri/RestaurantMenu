using System.ComponentModel.DataAnnotations;
using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RestaurantMenu.Filters;
using RestaurantMenu.Models;
using RestaurantMenu.Services;

namespace RestaurantMenu.Controllers;

/// <summary>
/// The Team section on Branch Details: the owner invites staff by email, changes their
/// role, removes them, or sends a new invite link. Only the branch owner may do any of
/// this (BranchPermission.ManageTeam). Removing someone takes effect on their next click:
/// access is checked against the database on every request.
/// </summary>
[Authorize(Roles = "OWNER,STAFF")]
[NoIndex]
public class TeamController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IBranchAccess _access;
    private readonly InviteMailer _mailer;

    public TeamController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IBranchAccess access, InviteMailer mailer)
    {
        _context = context;
        _userManager = userManager;
        _access = access;
        _mailer = mailer;
    }

    private IActionResult BackToTeam(int branchId) =>
        Redirect(Url.Action("Details", "Branch", new { id = branchId }) + "#team");

    private static string RoleName(BranchMemberRole role) => role == BranchMemberRole.Manager ? "Manager" : "Editor";

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Invite(int branchId, string? email, string? name, BranchMemberRole role)
    {
        if (!await _access.CanAsync(branchId, BranchPermission.ManageTeam))
        {
            return NotFound();
        }

        email = email?.Trim() ?? "";
        name = name?.Trim();
        if (email.Length == 0 || email.Length > 256 || !new EmailAddressAttribute().IsValid(email))
        {
            TempData["Error"] = "Enter a valid email address to invite someone.";
            return BackToTeam(branchId);
        }
        if (!Enum.IsDefined(role))
        {
            role = BranchMemberRole.Editor;
        }
        if (name is { Length: > 100 }) name = name[..100];

        var branch = await _context.Branches.AsNoTracking().Include(b => b.User).FirstAsync(b => b.Id == branchId);
        var normalized = _userManager.NormalizeEmail(email);
        // Including removed accounts: their email still belongs to them.
        var user = await _context.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.NormalizedEmail == normalized);

        if (user != null)
        {
            if (user.Id == branch.UserId)
            {
                TempData["Error"] = "That's your own account. You already have full access as the owner.";
                return BackToTeam(branchId);
            }
            if (user.IsDeleted)
            {
                TempData["Error"] = "That account was removed. Ask the administrator if it should come back.";
                return BackToTeam(branchId);
            }
            if (await _userManager.IsInRoleAsync(user, "ADMIN"))
            {
                TempData["Error"] = "Administrator accounts can't be added to a branch.";
                return BackToTeam(branchId);
            }
            var existing = await _context.BranchMembers.FirstOrDefaultAsync(m => m.BranchId == branchId && m.UserId == user.Id);
            if (existing != null)
            {
                TempData["Error"] = $"{user.Email} is already on the team as {RoleName(existing.Role)}.";
                return BackToTeam(branchId);
            }
        }

        var isNew = user == null;
        if (isNew)
        {
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FullName = string.IsNullOrWhiteSpace(name) ? email.Split('@')[0] : name,
                NIPT = "",
                NumberOfBranches = 0,
                EmailConfirmed = false
            };
            var created = await _userManager.CreateAsync(user);
            if (!created.Succeeded)
            {
                TempData["Error"] = string.Join(" ", created.Errors.Select(e => e.Description));
                return BackToTeam(branchId);
            }
        }

        // Staff can sign in to the back office; owners keep their own role as well.
        if (!await _userManager.IsInRoleAsync(user!, "STAFF") && !await _userManager.IsInRoleAsync(user!, "OWNER"))
        {
            await _userManager.AddToRoleAsync(user!, "STAFF");
        }

        _context.BranchMembers.Add(new BranchMember { BranchId = branchId, UserId = user!.Id, Role = role, CreatedUtc = DateTime.UtcNow });
        await _context.SaveChangesAsync();

        var inviter = WebUtility.HtmlEncode(branch.User?.FullName ?? "The owner");
        var branchName = WebUtility.HtmlEncode(branch.Name);
        if (isNew || user.PasswordHash == null)
        {
            await DeliverSetPassword(user, branch.Name,
                $"{inviter} added you to the team of {branchName} on My Quick Menu as {RoleName(role)}. Choose your password to sign in:");
        }
        else
        {
            var sent = await _mailer.SendNoticeAsync(user.Email!, $"You were added to {branch.Name}",
                $"<p>Hi {WebUtility.HtmlEncode(user.FullName)},</p><p>{inviter} added you to the team of {branchName} on My Quick Menu as {RoleName(role)}. " +
                "Sign in with your usual email and password; the branch is on your list.</p>");
            TempData["Success"] = $"{user.Email} is on the team as {RoleName(role)}" +
                                  (sent ? " and got an email about it." : ". They already have an account, so they can sign in right away.");
        }
        return BackToTeam(branchId);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeRole(int memberId, BranchMemberRole role)
    {
        var member = await _context.BranchMembers.Include(m => m.User).FirstOrDefaultAsync(m => m.Id == memberId);
        if (member == null || !await _access.CanAsync(member.BranchId, BranchPermission.ManageTeam))
        {
            return NotFound();
        }
        if (!Enum.IsDefined(role))
        {
            return BadRequest();
        }

        member.Role = role;
        await _context.SaveChangesAsync();
        TempData["Success"] = $"{member.User?.FullName} is now {RoleName(role)}.";
        return BackToTeam(member.BranchId);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(int memberId)
    {
        var member = await _context.BranchMembers.Include(m => m.User).FirstOrDefaultAsync(m => m.Id == memberId);
        if (member == null || !await _access.CanAsync(member.BranchId, BranchPermission.ManageTeam))
        {
            return NotFound();
        }

        // The account stays (it may be on other teams, and keeps its email); only this
        // branch's access goes.
        _context.BranchMembers.Remove(member);
        await _context.SaveChangesAsync();
        TempData["Success"] = $"{member.User?.FullName} no longer has access to this branch.";
        return BackToTeam(member.BranchId);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendInvite(int memberId)
    {
        var member = await _context.BranchMembers.Include(m => m.User).Include(m => m.Branch).FirstOrDefaultAsync(m => m.Id == memberId);
        if (member?.User == null || !await _access.CanAsync(member.BranchId, BranchPermission.ManageTeam))
        {
            return NotFound();
        }
        if (member.User.PasswordHash != null)
        {
            TempData["Error"] = $"{member.User.Email} has already set a password and can sign in.";
            return BackToTeam(member.BranchId);
        }

        await DeliverSetPassword(member.User, member.Branch!.Name,
            $"You were invited to the team of {WebUtility.HtmlEncode(member.Branch.Name)} on My Quick Menu. Choose your password to sign in:");
        return BackToTeam(member.BranchId);
    }

    private async Task DeliverSetPassword(ApplicationUser user, string branchName, string introHtml)
    {
        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var link = Url.Action("SetPassword", "Account", new { userId = user.Id, token }, Request.Scheme)!;
        if (await _mailer.SendSetPasswordAsync(user.Email!, user.FullName, $"Join {branchName} on My Quick Menu", introHtml, link))
        {
            TempData["Success"] = $"Invite sent to {user.Email}. The link works for 3 days.";
            return;
        }
        if (_mailer.CanEmail)
        {
            TempData["Warning"] = "The invite email could not be sent. Copy the link below and send it yourself.";
        }
        // Shown once in the Team section; the link signs them up, so treat it like a password.
        TempData["TeamInviteLink"] = link;
        TempData["TeamInviteFor"] = user.Email;
    }
}
