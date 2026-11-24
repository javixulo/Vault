---
name: container-delivery
description: Prepare the publication of a Docker image from GitHub Actions: publish the image that was tested, version tags and which one moves, multi-platform builds, a publishing job with least privilege, SBOM and provenance, vulnerability scanning that warns, a dry run with a throwaway registry that publishes nothing, and how consumers update and roll back. Language-neutral. Use this whenever the user wants to publish an image to a registry, create releases or tags for an image, set up a release workflow, or asks "how do I ship this container", even if they do not say CD. Making the app work in a container is covered by containerize-app; deploying to a server is out of scope.
---

# Container delivery

Delivery is the step where an image leaves your control, so it has to be exactly what you tested, identifiable by a version that never changes meaning, and published by a job that can do nothing else. This skill prepares that publication on GitHub Actions and the GitHub container registry; it does not deploy to any server. The image itself and its runtime behaviour are in `containerize-app`; the test pipeline is in `ci-pipeline`.

## 0. Ask before writing

Do not write a release workflow from guesses. Ask the human, proposing a default for each answer:

- The registry and the name of the image (**default: GitHub Container Registry, `ghcr.io/<owner>/<app>`**, lowercase). Why it is the default: the workflow logs in with its built-in token, so no long-lived registry password is stored; the package is tied to the repository; and public pulls have no rate limit. Another registry (Docker Hub) is a deliberate choice that adds a stored access token.
- How a release is created (**default: from the GitHub web page, Releases → Draft a new release, choosing "Create new tag on publish"**; the tag it creates starts the workflow. Pushing a version tag from a terminal does the same and must also work).
- The CPU architectures the image must run on, and which of them can be tested.
- Who pulls the image and how they update (by hand, by a scheduler, by an orchestrator).
- How much supply chain evidence they want (section 5).
- Whether anything must be published now, or only rehearsed (section 6).

## 1. Publish what was tested

An image rebuilt after the tests is a different image, even from the same commit. So the release workflow builds the image once, tests that very build (it starts, passes its startup checks, its health check answers, the main flows work), and publishes that build. Publishing is the last step, and it only happens if every earlier one succeeded. Identify the result by its digest, and use the digest for everything after the push (attestations, notes, reports).

Do not publish from a developer machine. A machine's state is not reproducible and leaves no record.

## 2. Versions and tags

- A release comes from an immutable version tag in version control, with a version number of the form MAJOR.MINOR.PATCH, optionally with a pre-release suffix. The image tags are derived from it by the workflow, never typed.
- Tags to publish: the full version, the minor (`1.2`), the major (`1`) and the commit. The major and minor tags move on purpose; the full version and the commit tag never do. (While the major is 0, the usual tag action skips the major tag; that is intended.)
- **The tag is the trigger, so protecting it is part of the design:** only the owner may create tags that match the version pattern (section 4, "Who can release").
- `latest` points only at the newest stable version: never at a pre-release.
- Never overwrite a published version tag. Rollback depends on an exact version still meaning the same image. If a release is wrong, publish a new one.
- Say in the README which tag to follow. Consumers who need exact repeatability pin the digest.

## 3. Platforms

Build for the architectures of the hosts that will run it, in one multi-platform image. Why: a host that pulls an image for another architecture either fails or runs it under slow emulation.

- Avoid compiling under emulation. If a build stage produces output that is the same on every architecture, run that stage on the machine that builds and only the final stage per platform.
- Test on the architecture the runner has. Say what is not tested (for example, the other architecture only starts, or is not run at all) instead of implying it.

## 4. A publishing job with least privilege

- A separate workflow, triggered only by version tags, with `contents: read` as the default. Only the publishing job gets `packages: write`, and only the attestation step gets what it needs (section 5).
- Log in with the built-in workflow token, not a personal one. No long-lived registry password in the repository.
- Pin third-party actions to a commit, as in `ci-pipeline`. Pass no secrets as build arguments.
- Image names in this registry are lowercase. Compute the name instead of typing it.
- After the first publication, check the package's visibility and that it is linked to the repository: new packages may start private, and the people who need to pull it cannot. Check it without logging in (ask the registry for the tag list anonymously): that is what a consumer will see. If it is private, either make the package public in its settings or tell consumers to log in once with a token that can read packages.

### The release workflow, step by step

Triggered only by a version tag, with these jobs in order, each failing the release if it fails:

1. **Verify.** The tag is a semantic version with a leading `v`; the tagged commit is in the main branch (fetch the full history to check); the tests that do not need the image pass. A tag on a stray commit must not publish.
2. **Build and test the image.** Build it for the runner's architecture and load it (not push); run the smoke test and the system tests on it.
3. **Scan** (warn only, section 5), **log in** with the built-in token and **push** the multi-platform image reusing the layers of step 2, with the tags of section 2, an SBOM and provenance. Write the digest and the tags in the job summary.
4. **GitHub release.** Its title and notes are the changelog entry of that version (see `versioning-and-changelog`). A person may already have created the release from the web page when making the tag, so the step must **edit it if it exists and create it if not**; a plain "create" fails on the first case. Mark it pre-release when the version has a suffix.

Two facts that cost a failed run: the workflow that runs is **the file at the tagged commit**, so a fix to the workflow must already be in the main branch before the tag is made; and a release created from the web page exists for a few minutes before its image does, so if the workflow fails before the push, delete the release and the tag and repeat with the same number (nothing was published). If the image was already pushed, the number is spent: publish the next one.

### Who can release

- A tag ruleset that restricts creating tags matching `v*` to the owner (or to the maintainers' role). Whoever can create the tag can publish an image, so this is the real release permission.
- Pull requests from forks run no workflow until a maintainer approves them (repository Actions settings), and the publishing job is unreachable from pull requests anyway.
- Branch protection of the main branch is in `ci-pipeline`.

## 5. Supply chain evidence

Proportional to the audience. Default for a small project:

- **Basics:** pinned base, no secrets in layers, non-root user (these are in `containerize-app`).
- **SBOM and provenance:** ask the build action to attach both to the image. The SBOM lists what the image contains; the provenance records how and from which commit it was built. They cost almost nothing and make a later security question answerable.
- **Vulnerability scan of the image before publishing.** Report the findings in the job summary and warn. Block only on a severity the human chose, and only for fixable findings, otherwise the release depends on other people's timing.
- **Signing** is out of scope by default: it only helps if consumers verify the signature. Add it when someone will.

## 6. Rehearse before publishing for real

A manual workflow that does everything except publish: it starts a throwaway registry as a service of the job, builds the multi-platform image, pushes it there, pulls it back, runs the same tests on the pulled image, and writes in the job summary the tags it would publish and the manifest it pushed. It has read-only permissions and no login to any real registry.

It proves the build, the tags, the platforms and the push and pull mechanics. It cannot prove the login, the permissions and the visibility of the real registry; the first real release is the only test of those, so make it a pre-release, and say so in the spec. Order: rehearsal, first release as a pre-release, run that image where it will really run, then the first stable release. A pre-release never moves `latest`, so consumers who try it name its exact version.

## 7. Updating and rolling back

Delivery ends where the consumer starts, so write down for them:

- How to update (pull a specific version, recreate the container) and how to see which version runs (labels, the logs, or the version a protocol handshake reports). The consumer's compose file pins the version in a required variable (`containerize-app`, section 7.1): updating is changing that number, a decision.
- Deployment to the consumer's machine is the consumer's compose file, not the pipeline's job: the pipeline never gets a credential for their server. Recommend against automatic updaters that follow `latest` for anything holding a powerful credential; offer, at most, a notification of new releases.
- That the previous image stays on the host until the new one is confirmed, so going back is starting the old tag.
- What changed in each release: a short changelog that names changes to configuration, mounted folders or the user, because those break a running installation.

## 8. Check it

- Is the published image the tested one? Can you show it by digest?
- Does a version tag push produce the expected tags, and `latest` only for a stable version?
- Is any step able to publish from a branch, a pull request or a fork?
- Does only the publishing job have write permissions, with the built-in token?
- Do the platforms match the hosts, and is what was not tested stated?
- Does the rehearsal run without any credential and publish nothing?
- Can a consumer find the version running, update, and go back, from the README alone?
- Does the release step cope with a release that already exists? Does a tag on a commit outside the main branch fail in the first job?
- Can only the owner create a version tag? Is the package pullable the way the README says, checked anonymously?
- Is the workflow itself linted (a tool such as actionlint) before it is tagged?
