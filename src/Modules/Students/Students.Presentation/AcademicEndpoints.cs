using Asp.Versioning;
using Asp.Versioning.Builder;
using BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Students.Application.Academics;

namespace Students.Presentation;

/// <summary>
/// Academic catalog endpoints: instructors, courses (with prerequisites), and course sections (with the
/// enrollment / waitlist roster). All within the Students module/DB.
/// </summary>
public static class AcademicEndpoints
{
    public static IEndpointRouteBuilder MapAcademicEndpoints(this IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        var instructors = app.MapGroup("")
            .WithTags("Instructors")
            .WithApiVersionSet(versionSet)
            .HasApiVersion(new ApiVersion(1, 0));

        var courses = app.MapGroup("")
            .WithTags("Courses")
            .WithApiVersionSet(versionSet)
            .HasApiVersion(new ApiVersion(1, 0));

        var sections = app.MapGroup("")
            .WithTags("Sections")
            .WithApiVersionSet(versionSet)
            .HasApiVersion(new ApiVersion(1, 0));

        var transcripts = app.MapGroup("")
            .WithTags("Transcripts")
            .WithApiVersionSet(versionSet)
            .HasApiVersion(new ApiVersion(1, 0));

        // ---- Instructors ----------------------------------------------------------------------------

        instructors.MapPost("/instructors", async (
            CreateInstructor.Command command,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(command, cancellationToken);
            return Results.Created($"/instructors/{id}", new { id });
        })
        .WithName("CreateInstructor")
        .WithSummary("Add an instructor to the directory.")
        .RequireAuthorization();

        // ---- Courses --------------------------------------------------------------------------------

        courses.MapPost("/courses", async (
            CreateCourse.Command command,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(command, cancellationToken);
            return Results.Created($"/courses/{id}", new { id });
        })
        .WithName("CreateCourse")
        .WithSummary("Add a course to the catalog.")
        .RequireAuthorization();

        courses.MapGet("/courses/{courseId:guid}", async (
            Guid courseId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var course = await sender.Send(new GetCourse.Query(courseId), cancellationToken);
            return course is null ? Results.NotFound() : Results.Ok(course);
        })
        .WithName("GetCourse")
        .WithSummary("A course with its prerequisite list.");

        courses.MapPost("/courses/search", async (
            SearchCourses.Query query,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(query, cancellationToken);
            return Results.Ok(result);
        })
        .WithName("SearchCourses")
        .WithSummary("Paged catalog search, optionally filtered by department (paging/filter in the body).");

        courses.MapPost("/courses/{courseId:guid}/prerequisites", async (
            Guid courseId,
            AddPrerequisiteRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(
                new AddCoursePrerequisite.Command(courseId, request.PrerequisiteCourseId), cancellationToken);
            return Results.Ok(new { courseId = id });
        })
        .WithName("AddCoursePrerequisite")
        .WithSummary("Declare a prerequisite for a course (enforced at section enrollment).")
        .RequireAuthorization();

        // ---- Sections -------------------------------------------------------------------------------

        sections.MapPost("/sections", async (
            ScheduleSection.Command command,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(command, cancellationToken);
            return Results.Created($"/sections/{id}", new { id });
        })
        .WithName("ScheduleSection")
        .WithSummary("Open a section of a course for a term (instructor, capacity, schedule).")
        .RequireAuthorization();

        sections.MapGet("/sections/{sectionId:guid}", async (
            Guid sectionId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var section = await sender.Send(new GetSection.Query(sectionId), cancellationToken);
            return section is null ? Results.NotFound() : Results.Ok(section);
        })
        .WithName("GetSection")
        .WithSummary("A section with course, instructor, schedule, capacity, and live enrolled/waitlist counts.");

        sections.MapPost("/sections/search", async (
            SearchSections.Query query,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(query, cancellationToken);
            return Results.Ok(result);
        })
        .WithName("SearchSections")
        .WithSummary("Paged section search by term/course/instructor (paging/filters in the body).");

        sections.MapGet("/sections/{sectionId:guid}/roster", async (
            Guid sectionId,
            int? page,
            int? pageSize,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var roster = await sender.Send(
                new GetSectionRoster.Query(sectionId, page ?? 1, pageSize ?? 20), cancellationToken);
            return Results.Ok(roster);
        })
        .WithName("GetSectionRoster")
        .WithSummary("Paged roster (enrolled + waitlisted) for a section. Paged via ?page=&pageSize= (default 1/20, max 100).");

        sections.MapPost("/sections/{sectionId:guid}/enroll", async (
            Guid sectionId,
            EnrollRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(
                new EnrollInSection.Command(sectionId, request.StudentId), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("EnrollInSection")
        .WithSummary("Enroll a student (takes a seat, or joins the waitlist if full). Enforces prerequisites.")
        .RequireAuthorization();

        sections.MapPost("/sections/{sectionId:guid}/drop", async (
            Guid sectionId,
            EnrollRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(new DropSection.Command(sectionId, request.StudentId), cancellationToken);
            return Results.Ok(new { sectionId = id });
        })
        .WithName("DropSection")
        .WithSummary("Drop a student from a section; promotes the next waitlisted student into the freed seat.")
        .RequireAuthorization();

        sections.MapPost("/sections/{sectionId:guid}/cancel", async (
            Guid sectionId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(new CancelSection.Command(sectionId), cancellationToken);
            return Results.Ok(new { sectionId = id });
        })
        .WithName("CancelSection")
        .WithSummary("Cancel a section so no further enrollment is possible.")
        .RequireAuthorization();

        sections.MapGet("/students/{studentId:guid}/schedule", async (
            Guid studentId,
            string term,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var schedule = await sender.Send(new GetStudentSchedule.Query(studentId, term), cancellationToken);
            return Results.Ok(schedule);
        })
        .WithName("GetStudentSchedule")
        .WithSummary("A student's enrolled sections for a term (?term=...).");

        sections.MapPost("/sections/{sectionId:guid}/grades", async (
            Guid sectionId,
            RecordGradeRequest request,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var result = await sender.Send(
                new RecordGrade.Command(sectionId, request.StudentId, request.Grade), cancellationToken);
            return Results.Ok(result);
        })
        .WithName("RecordGrade")
        .WithSummary("Record a letter grade for an enrolled student (completes the enrollment).")
        .RequireAuthorization();

        // ---- Transcripts & standing -----------------------------------------------------------------

        transcripts.MapGet("/students/{studentId:guid}/transcript", async (
            Guid studentId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var transcript = await sender.Send(new GetTranscript.Query(studentId), cancellationToken);
            return Results.Ok(transcript);
        })
        .WithName("GetTranscript")
        .WithSummary("A student's transcript: graded courses, cumulative GPA, credits, and standing.");

        transcripts.MapGet("/students/{studentId:guid}/standing", async (
            Guid studentId,
            ISender sender,
            CancellationToken cancellationToken) =>
        {
            var standing = await sender.Send(new GetAcademicStanding.Query(studentId), cancellationToken);
            return Results.Ok(standing);
        })
        .WithName("GetAcademicStanding")
        .WithSummary("A student's GPA summary and academic standing (the light projection of the transcript).");

        return app;
    }

    public sealed record AddPrerequisiteRequest(Guid PrerequisiteCourseId);

    public sealed record EnrollRequest(Guid StudentId);

    public sealed record RecordGradeRequest(Guid StudentId, string Grade);
}
