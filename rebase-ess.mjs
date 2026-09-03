#!/usr/bin/env node
import { execSync } from 'node:child_process';

// ESS lab branch chain. Each starter branch is the solution of the
// previous lab and the starting point for the next one. Rebasing the
// chain top-to-bottom propagates changes made to ess-starter (or any
// earlier branch) downstream through every lab up to ess-solution.
const tasks = [
  { branch: 'ess-intro-starter', base: 'ess-starter' },
  { branch: 'ess-signals-starter', base: 'ess-intro-starter' },
  { branch: 'ess-services-starter', base: 'ess-signals-starter' },
  { branch: 'ess-pipes-starter', base: 'ess-services-starter' },
  { branch: 'ess-components-starter', base: 'ess-pipes-starter' },
  { branch: 'ess-testing-starter', base: 'ess-components-starter' },
  { branch: 'ess-routing-starter', base: 'ess-testing-starter' },
  { branch: 'ess-solution', base: 'ess-routing-starter' },
];

const args = process.argv.slice(2);
const check = args.includes('--check');
const doBuild = check || args.includes('--build');
const doLint = check || args.includes('--lint');

function run(cmd) {
  console.log(`\n> ${cmd}`);
  execSync(cmd, { stdio: 'inherit' });
}

function runSafe(cmd, branch, label) {
  try {
    run(cmd);
  } catch {
    console.error(`\n❌ ${label} failed on branch ${branch}. Aborting.`);
    process.exit(1);
  }
}

function parseSkipArgs(argv) {
  const skips = [];
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];

    if (arg.startsWith('--skip=')) {
      skips.push(arg.slice('--skip='.length));
      continue;
    }

    if (arg !== '--skip') {
      continue;
    }

    const value = argv[++i];
    if (value && !value.startsWith('--')) {
      skips.push(value);
      continue;
    }

    console.error(
      '❌ --skip benötigt einen Wert (z. B. --skip ess-pipes-starter).',
    );
    process.exit(1);
  }
  return skips;
}

function main() {
  const skips = parseSkipArgs(args);

  // Sicherheitscheck: sauberes Working Tree
  try {
    const status = execSync('git status --porcelain', {
      encoding: 'utf8',
    }).trim();
    if (status.length > 0) {
      console.error(
        '❌ Working tree ist nicht clean. Bitte commit/stash vorher.',
      );
      process.exit(1);
    }
  } catch {
    console.error('❌ Kein Git-Repo oder git nicht verfügbar.');
    process.exit(1);
  }

  const startBranch = execSync('git branch --show-current', {
    encoding: 'utf8',
  }).trim();

  const filteredTasks = tasks.filter(({ branch }) => {
    const match = skips.find((s) => branch.includes(s));
    if (match) {
      console.log(`⏭  Skip: ${branch} (matcht --skip ${match})`);
      return false;
    }
    return true;
  });

  for (const { branch, base } of filteredTasks) {
    console.log(`\n==============================`);
    console.log(`Rebase: ${branch}  <-  ${base}`);
    console.log(`==============================`);

    // branch auschecken
    run(`git checkout ${branch}`);

    // Rebase
    run(`git rebase ${base}`);

    console.log(`✅ OK: ${branch} rebased on ${base}`);

    if (doBuild) {
      runSafe('npx ng build', branch, 'Build');
      console.log(`✅ Build OK: ${branch}`);
    }

    if (doLint) {
      runSafe('npx ng lint', branch, 'Lint');
      console.log(`✅ Lint OK: ${branch}`);
    }
  }

  // zurück zum Start-Branch
  if (startBranch) {
    run(`git checkout ${startBranch}`);
  }

  console.log('\n🎉 Fertig.');
}

main();
