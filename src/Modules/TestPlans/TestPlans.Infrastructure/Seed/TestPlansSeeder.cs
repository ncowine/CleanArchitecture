using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TestPlans.Domain;
using TestPlans.Infrastructure.Persistence;

namespace TestPlans.Infrastructure.Seed;

/// <summary>
/// Seeds a small, representative content tree so the POC runs end-to-end without manual authoring:
/// one plan, two versions, two platforms, a couple of categories/sub-categories, and a few tasks
/// (including one multiplayer task). Idempotent — a no-op once any plan exists.
/// </summary>
public static class TestPlansSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var db = services.GetRequiredService<TestPlansDbContext>();

        if (await db.Plans.AnyAsync(cancellationToken))
        {
            return;
        }

        var plan = TestPlan.Create("Core Gameplay Test Plan", "CORE");
        db.Plans.Add(plan);

        db.Versions.AddRange(
            TestPlanVersion.Create(plan.Id, 1, 0),
            TestPlanVersion.Create(plan.Id, 1, 1));

        db.Platforms.AddRange(
            Platform.Create("PC", "PC"),
            Platform.Create("Xbox", "XBX"));

        var movement = Category.Create(plan.Id, "Movement", 1);
        var combat = Category.Create(plan.Id, "Combat", 2);
        db.Categories.AddRange(movement, combat);

        var walking = SubCategory.Create(movement.Id, "Walking & Running", 1);
        var jumping = SubCategory.Create(movement.Id, "Jumping", 2);
        var melee = SubCategory.Create(combat.Id, "Melee", 1);
        db.SubCategories.AddRange(walking, jumping, melee);

        db.Tasks.AddRange(
            TestTask.Create(walking.Id, "Walk in all directions",
                "Verify the character walks smoothly in all eight directions.", TaskMode.SinglePlayer),
            TestTask.Create(jumping.Id, "Jump across a standard gap",
                "Verify the jump distance clears the standard gap without falling.", TaskMode.SinglePlayer),
            TestTask.Create(melee.Id, "Co-op melee combo",
                "Two players chain a melee combo together in the same session.", TaskMode.Multiplayer));

        await db.SaveChangesAsync(cancellationToken);
    }
}
