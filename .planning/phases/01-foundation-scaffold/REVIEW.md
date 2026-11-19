---
phase: 01-foundation-scaffold
reviewed: 2026-11-19T19:02:27Z
depth: quick
files_reviewed: 38
files_reviewed_list:
  - .gitignore
  - Directory.Build.props
  - KargoNet.sln
  - src/BuildingBlocks/KargoNet.Contracts/KargoNet.Contracts.csproj
  - src/BuildingBlocks/KargoNet.Contracts/Models/ShipmentStatus.cs
  - src/BuildingBlocks/KargoNet.Messaging/KargoNet.Messaging.csproj
  - src/Services/Shipment.Api/Program.cs
  - src/Services/Shipment.Api/Shipment.Api.csproj
  - src/Services/Shipment.Api/appsettings.json
  - src/Services/Shipment.Api/appsettings.Development.json
  - src/Services/Tracking.Api/Program.cs
  - src/Services/Tracking.Api/Tracking.Api.csproj
  - src/Services/Tracking.Api/appsettings.json
  - src/Services/Tracking.Api/appsettings.Development.json
  - src/Web/Portal.Web/Program.cs
  - src/Web/Portal.Web/Portal.Web.csproj
  - src/Web/Portal.Web/Controllers/HomeController.cs
  - src/Web/Portal.Web/Models/ErrorViewModel.cs
  - src/Web/Portal.Web/Views/Home/Index.cshtml
  - src/Web/Portal.Web/Views/Shared/Error.cshtml
  - src/Web/Portal.Web/appsettings.json
  - src/Web/Portal.Web/appsettings.Development.json
  - src/Web/operator-ui/package.json
  - src/Web/operator-ui/angular.json
  - src/Web/operator-ui/tsconfig.json
  - src/Web/operator-ui/tsconfig.app.json
  - src/Web/operator-ui/tsconfig.spec.json
  - src/Web/operator-ui/src/main.ts
  - src/Web/operator-ui/src/index.html
  - src/Web/operator-ui/src/app/app.ts
  - src/Web/operator-ui/src/app/app.html
  - src/Web/operator-ui/src/app/app.config.ts
  - src/Web/operator-ui/src/app/app.routes.ts
  - src/Web/operator-ui/src/app/app.scss
  - src/Web/operator-ui/src/app/app.spec.ts
  - src/Web/operator-ui/src/styles.scss
  - tests/KargoNet.Tracking.Tests/TrackingTests.cs
  - tests/KargoNet.Tracking.Tests/KargoNet.Tracking.Tests.csproj
findings:
  critical: 0
  warning: 2
  info: 0
  total: 2
status: issues_found
---

# Phase 01: Code Review Report

**Reviewed:** 2026-11-19T19:02:27Z
**Depth:** quick
**Files Reviewed:** 38
**Status:** issues_found

## Summary

Reviewed the hand-authored source for Phase 1 (Foundation & Scaffold): the solution file, `Directory.Build.props`, `.gitignore`, the two empty BuildingBlock library projects (Contracts + Messaging), the three ASP.NET Core service/web entry points (Shipment.Api, Tracking.Api, Portal.Web), the Angular `operator-ui` scaffold, and the single xUnit test project.

This is intentionally a thin scaffold — most projects are empty shells (entry points, one enum, one test). Framework usage is correct for the .NET 10 / Angular 22 targets: `MapStaticAssets()`/`.WithStaticAssets()` is the current .NET static-asset pattern, `Privacy()` has a matching `Privacy.cshtml` view, and the MVC view infrastructure (`_Layout`, `_ViewStart`, `_ViewImports`, `_ValidationScriptsPartial`) is complete. No security exposures are present that are meaningful for a no-auth internal scaffold (`AllowedHosts: "*"` is the standard template default).

Two real defects were found, both in the test surface. Neither blocks the scaffold from building or running, but both will cause the test suites to fail when executed.

## Warnings

### WR-01: Angular unit test will fail — `App` renders `<router-outlet>` but the `TestBed` does not provide the router

**File:** `src/Web/operator-ui/src/app/app.spec.ts:6-9`
**Issue:** The hand-retargeted `App` component imports `RouterOutlet` (`src/app/app.ts:2,5`) and its template renders `<router-outlet />` (`src/app/app.html:5`). The scaffold `app.spec.ts` was not updated to match: `TestBed.configureTestingModule({ imports: [App] })` provides no router (`provideRouter`). `RouterOutlet`'s constructor injects the `Router` service, so `TestBed.createComponent(App)` throws a DI error (NG0201/NG0800), causing **both** tests (`should create the app` and `should render title`) to fail. This is the classic case of the default CLI scaffold test working only because the stock `App` had no `RouterOutlet`; the retarget added the outlet without updating the test.
**Fix:** Provide an empty router in the test config so `RouterOutlet` can resolve:
```ts
import { provideRouter } from '@angular/router';

beforeEach(async () => {
  await TestBed.configureTestingModule({
    imports: [App],
    providers: [provideRouter()],
  }).compileComponents();
});
```
(Alternatively, reuse the real `appConfig` providers in the test module.)

### WR-02: Test project mixes xUnit v2 framework with the xUnit v3 test runner (major-version mismatch)

**File:** `tests/KargoNet.Tracking.Tests/KargoNet.Tracking.Tests.csproj:13-14`
**Issue:** `xunit` is at major version 2 (`2.9.3`) while `xunit.runner.visualstudio` is at major version 3 (`3.1.4`). These are different major versions of the xUnit ecosystem. The v3 runner is built for the xUnit v3 test SDK/packages and is not a supported combination with the v2 `xunit` package; in practice this can prevent the test assembly from being discovered and run (a silently-green or non-executing `dotnet test`). The single test (`TrackingTests`) may therefore never actually run.
**Fix:** Align both packages to the same major version. Either:
- upgrade to v3: `xunit.v3` + `xunit.v3.runner.visualstudio` (v3 naming), **or**
- pin the runner to a matching v2 line: `xunit` `2.9.3` + `xunit.runner.visualstudio` `2.x` (e.g. `2.8.2`).
Then run `dotnet test` once to confirm the test is discovered and passes.

---

_Reviewed: 2026-11-19T19:02:27Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: quick_
