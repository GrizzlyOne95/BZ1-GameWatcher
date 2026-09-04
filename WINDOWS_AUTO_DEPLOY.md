# Windows production auto-deploy

The production Tailscale-hosted Game Watcher can deploy automatically after a successful push to
`main` without exposing WinRM, SSH, or another inbound management service.

## Deployment flow

```text
push / merge to main
        |
        v
Build, test and publish
  - API tests/build
  - Web tests/build
  - GHCR images
  - Windows deployment artifact
        |
        | only when the whole workflow succeeds
        v
Deploy Windows service
  - dedicated self-hosted runner on production host
  - downloads the artifact from the successful workflow run
  - verifies the artifact commit SHA
  - preserves appsettings.Production.json
  - stops BZ1GameWatcher
  - swaps the tested release into C:\Services\BZ1GameWatcher\current
  - reapplies service/secret ACLs
  - starts BZ1GameWatcher
  - polls http://127.0.0.1:5283/api/health
  - automatically restores the previous release if health validation fails
```

The deploy workflow accepts only successful **push** runs whose head branch is `main`. Pull requests
never receive the production runner and never create a production deployment.

## One-time production runner setup

GitHub requires one self-hosted runner on the Windows machine that already hosts the
`BZ1GameWatcher` Windows service.

1. In this repository, open **Settings -> Actions -> Runners -> New self-hosted runner**.
2. Select **Windows** and **x64**.
3. On the production machine, open an elevated PowerShell window.
4. Install the runner outside the application release tree, preferably:

   ```powershell
   New-Item -ItemType Directory -Path C:\actions-runner\bz1-gamewatcher -Force
   Set-Location C:\actions-runner\bz1-gamewatcher
   ```

5. Run the download/extract commands GitHub shows on the runner setup page. Those commands contain
   the current runner version and should be copied from GitHub rather than hard-coded here.
6. Run GitHub's generated `config.cmd` command, adding the production label:

   ```text
   bz1-gamewatcher-prod
   ```

   Use a descriptive runner name such as:

   ```text
   BZ1GameWatcher-Prod
   ```

7. When `config.cmd` asks whether to run the runner as a Windows service, choose **Yes**. GitHub's
   Windows runner setup requires an elevated shell to configure service mode.
8. The account running the runner service must have local administrator rights for the initial
   implementation because `deploy-windows-service.ps1` must stop/start `BZ1GameWatcher` and maintain
   the existing release/secret ACLs.
9. Verify the repository's **Settings -> Actions -> Runners** page shows the runner as **Idle** and
   that it has these labels:

   ```text
   self-hosted
   Windows
   X64
   bz1-gamewatcher-prod
   ```

Do not install the runner under `C:\Services\BZ1GameWatcher\current`; that directory is intentionally
replaced during deployment.

## Security boundary

This repository is public, so the production runner is deliberately referenced only by the separate
`Deploy Windows service` workflow. Pull-request jobs continue to use GitHub-hosted runners.

The production deployment job additionally requires all of the following:

- upstream workflow conclusion is `success`;
- upstream event is `push`;
- upstream branch is `main`;
- the downloaded artifact name contains the exact successful commit SHA;
- `_deployment_commit.txt` inside the artifact matches that SHA;
- the artifact must not contain `appsettings.Production.json`.

The production Steam API key remains only on the production host. The deployment copies the existing
protected `appsettings.Production.json` from the prior release into the new release and reapplies the
same restricted ACL model used by `install-windows-service.ps1`.

Because a self-hosted runner can execute trusted repository workflow code on the host, restrict write
access to this repository and keep branch protection on `main`. If the production machine later
hosts unrelated sensitive workloads, move the runner to a dedicated deployment VM or replace the
administrator runner account with a narrowly delegated service account.

## What happens on failure

A release is not considered successful merely because the process starts. The deploy script waits for
`/api/health` to return a successful HTTP response. If startup or health validation fails:

1. the failed service is stopped;
2. the failed release is moved out of `current`;
3. the previous release is restored;
4. its ACLs are reapplied;
5. the service is restarted and health-checked again;
6. the GitHub Actions deployment job fails visibly.

Successful deployments retain the three most recent previous release directories under:

```text
C:\Services\BZ1GameWatcher\rollback
```

Activity history under `C:\Services\BZ1GameWatcher\data` is never part of the release swap.
