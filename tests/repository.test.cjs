const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '..');
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8').replace(/^\uFEFF/, '');

test('fault regressions use typed services instead of private-member reflection', () => {
  assert.equal(fs.existsSync(path.join(root, 'tests/TestApplication.cs')), false,
    'Do not restore the private-state compatibility adapter');
  for (const file of fs.readdirSync(path.join(root, 'tests'))) {
    if (file.endsWith('.cs')) {
      assert.doesNotMatch(read(`tests/${file}`), /\bBindingFlags\s*\.\s*NonPublic|\.\s*Get(?:Field|Property|Method)s?\s*\(/,
        `${file}: prepare faults through real files, injected dependencies and runtime lifecycle`);
    } else if (file.endsWith('.ps1')) {
      assert.doesNotMatch(read(`tests/${file}`), /TestApplication\.cs/,
        `${file}: must not compile the retired adapter`);
    }
  }
});

test('release, executable source, interface and current documentation use one version and author', () => {
  const assembly = read('src/AssemblyInfo.cs');
  const version = assembly.match(/AssemblyInformationalVersion\("(\d+\.\d+\.\d+)"\)/)?.[1];
  assert.ok(version, 'AssemblyInformationalVersion must be a release version');
  for (const attribute of ['AssemblyVersion', 'AssemblyFileVersion']) {
    assert.equal(assembly.match(new RegExp(`${attribute}\\("([^\"]+)"\\)`))?.[1], version + '.0', attribute);
  }
  const developmentPackage = JSON.parse(read('package.json'));
  const dependencyLock = JSON.parse(read('package-lock.json'));
  assert.equal(developmentPackage.version, version, 'Development package version');
  assert.equal(dependencyLock.version, version, 'Dependency lock version');
  assert.equal(dependencyLock.packages[''].version, version, 'Root dependency lock version');
  const author = assembly.match(/AssemblyCompany\("([^"]+)"\)/)?.[1];
  assert.equal(author, 'FrancisChen');
  const status = JSON.parse(read('release-status.json'));
  assert.equal(status.version, version);
  assert.equal(status.author, author);
  assert.equal(read('src/Settings.cs').match(/const string AppVersion\s*=\s*"([^"]+)"/)?.[1], version);
  const homepage = read('web/index.html');
  assert.equal(homepage.match(/<span class="brand-name">Luna<\/span><small>([^<]+)<\/small>/)?.[1], version, 'Header version');
  assert.equal(homepage.match(/<dd id="about-version">([^<]+)<\/dd>/)?.[1], version, 'About fallback version');
  const helpVersions = [...homepage.matchAll(/· 版本 (\d+\.\d+\.\d+)。/g)].map(match => match[1]);
  assert.ok(helpVersions.length > 0, 'Help version must remain covered by the version guard');
  for (const helpVersion of helpVersions) assert.equal(helpVersion, version, 'Help version');
  assert.equal(require('../web/locales.js')[`· 版本 ${version}。`], `· Version ${version}.`, 'English help version');
  assert.equal(read('src/app.manifest').match(/<assemblyIdentity\b[^>]*version="([^"]+)"/)?.[1], version + '.0', 'Windows manifest version');
  for (const file of ['web/index.html','README.md','README.zh-CN.md','docs/USER_GUIDE.md','docs/USER_GUIDE.zh-CN.md','docs/DEVELOPER_GUIDE.md','docs/DEVELOPER_GUIDE.zh-CN.md']) {
    assert.ok(read(file).includes(version), `${file} must describe the current version`);
  }
  for (const file of [`docs/QA-${version}.md`, `docs/RELEASE-v${version}.md`]) assert.ok(fs.existsSync(path.join(root, file)), file);
});

test('current documentation local links and image files exist', () => {
  const files = ['README.md','README.zh-CN.md','CHANGELOG.md','THIRD_PARTY_NOTICES.md',
    'docs/USER_GUIDE.md','docs/USER_GUIDE.zh-CN.md','docs/DEVELOPER_GUIDE.md','docs/DEVELOPER_GUIDE.zh-CN.md','docs/REPOSITORY_SETUP.md'];
  for (const file of files) {
    const content = read(file).replace(/```[^]*?```/g, '');
    for (const match of content.matchAll(/!?\[[^\]\n]*\]\(([^)\s]+)\)/g)) {
      const target = match[1];
      if (/^(?:[a-z][a-z0-9+.-]*:|#)/i.test(target)) continue;
      const clean = decodeURIComponent(target.split(/[?#]/)[0]);
      const resolved = path.resolve(root, path.dirname(file), clean);
      assert.ok(resolved.startsWith(root + path.sep), `${file}: link escapes the source tree`);
      assert.ok(fs.existsSync(resolved), `${file}: missing ${target}`);
    }
  }
});

test('homepage links belong to Luna while reference projects remain in acknowledgements', () => {
  const home = 'https://github.com/Francis-Chenyj/Luna-dev';
  for (const file of ['README.md','README.zh-CN.md','docs/USER_GUIDE.md','docs/USER_GUIDE.zh-CN.md','docs/DEVELOPER_GUIDE.md','docs/DEVELOPER_GUIDE.zh-CN.md']) {
    assert.ok(read(file).includes(home), `${file}: Luna homepage is missing`);
  }
  for (const file of ['README.md','README.zh-CN.md']) {
    const content = read(file);
    const creditIndex = Math.max(content.indexOf('## Author and acknowledgements'),content.indexOf('## 作者与致谢'));
    assert.ok(creditIndex > 0);
    for (const repo of ['LeagueAkari/LeagueAkari','CharryLee0426/yifan-ad-skipper','CharryLee0426/codex_team_token_tracker']) {
      assert.ok(content.indexOf('https://github.com/' + repo) > creditIndex, `${file}: reference link outside acknowledgements`);
    }
  }
});
