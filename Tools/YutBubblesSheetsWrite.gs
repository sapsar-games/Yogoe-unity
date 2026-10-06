/**
 * 한폭요괴 기획 시트용 Apps Script — 시트 쓰기(개발 도구 npm run *:push) + 「한폭요괴」 메뉴(게임에 반영).
 *
 * 설치 (한 번) — 반드시 해당 스프레드시트에서:
 * 1) https://docs.google.com/spreadsheets/d/1d3c7nN8cZjKQUBBetL5B7q6U2hwUBRvwWtL5ys4nrRs
 *    → 확장 프로그램 → Apps Script  (다른 프로젝트에 붙이면 안 됨)
 * 2) 기존 코드 지우고 이 파일 전체를 붙여넣기 → 저장(💾)
 * 3) 배포 → 새 배포 → 유형: 웹 앱
 *    - 설명: yut bubbles write
 *    - 실행 계정: 나
 *    - 액세스 권한: 모든 사용자  ← "나만"/"Google 계정 사용자"면 실패
 * 4) 권한 승인(필요 시) 후 웹 앱 URL(.../exec) 복사
 * 5) 브라우저에서 그 URL을 연다 → {"ok":true,"service":"yut-bubbles-write"} 가 보여야 정상
 * 6) Tools/yut_bubbles_sheets.config.json 의 write_url 에 그 URL 저장
 * 7) 코드를 고친 뒤에는 배포 관리 → 연필 → 버전: 새 버전 → 배포
 *
 * 로컬: npm run yut-bubbles:push
 *
 * ── 「한폭요괴 → 게임에 반영」 메뉴 (기획자가 시트에서 바로 배포) ──
 * 설치 (한 번):
 * a) GitHub → Settings → Developer settings → Fine-grained personal access token → Generate
 *    - Repository access: Only select repositories → sapsar-games/Yogoe-unity
 *    - Permissions → Repository → Actions: Read and write (다른 권한은 필요 없음)
 * b) Apps Script → 프로젝트 설정(⚙) → 스크립트 속성 → 속성 추가
 *    - 이름: GITHUB_TOKEN   값: a) 의 토큰
 * c) 저장 후 시트를 새로고침하면 메뉴 「한폭요괴」가 생긴다. 처음 누를 때 권한 승인.
 * 누르면 GitHub Actions 'Sheets → Deploy' 가 시트 검사 → 커밋 → 빌드 → 배포하고, deploy_status 탭에 결과를 적는다.
 */

var GITHUB_REPO = 'sapsar-games/Yogoe-unity';
var DEPLOY_WORKFLOW = 'sheets-deploy.yml';

var SPREADSHEET_ID = '1d3c7nN8cZjKQUBBetL5B7q6U2hwUBRvwWtL5ys4nrRs';

function onOpen() {
  SpreadsheetApp.getUi().createMenu('한폭요괴')
    .addItem('게임에 반영 (검사 → 배포, 약 12분)', 'requestDeploy')
    .addItem('반영 기록 보기', 'showDeployStatus')
    .addSeparator()
    .addItem('(개발) 쓰기 엔드포인트 안내', 'showWriteHelp')
    .addToUi();
}

/** 메뉴: GitHub Actions 'Sheets → Deploy' 실행 요청. */
function requestDeploy() {
  const ui = SpreadsheetApp.getUi();
  const token = PropertiesService.getScriptProperties().getProperty('GITHUB_TOKEN');
  if (!token) {
    ui.alert('스크립트 속성 GITHUB_TOKEN 이 없습니다.\n개발자에게 설치를 부탁하세요 (Tools/YutBubblesSheetsWrite.gs 맨 위 안내).');
    return;
  }
  const ok = ui.alert('게임에 반영할까요?',
    '시트 전체를 검사해서 문제없는 탭을 게임에 반영하고 배포해요.\n약 12분 뒤 deploy_status 탭에 \'배포 완료\'가 떠요.',
    ui.ButtonSet.OK_CANCEL);
  if (ok !== ui.Button.OK) return;

  var who = '';
  try { who = Session.getActiveUser().getEmail().split('@')[0]; } catch (e) {}
  const res = UrlFetchApp.fetch(
    'https://api.github.com/repos/' + GITHUB_REPO + '/actions/workflows/' + DEPLOY_WORKFLOW + '/dispatches', {
      method: 'post',
      contentType: 'application/json',
      headers: { Authorization: 'Bearer ' + token, Accept: 'application/vnd.github+json' },
      payload: JSON.stringify({ ref: 'main', inputs: { requester: who } }),
      muteHttpExceptions: true,
    });
  if (res.getResponseCode() === 204) {
    writeDeployStatus_('요청됨', '시트 검사를 시작해요' + (who ? ' (' + who + ')' : ''),
      'https://github.com/' + GITHUB_REPO + '/actions/workflows/' + DEPLOY_WORKFLOW);
    SpreadsheetApp.getActive().toast('요청했어요. deploy_status 탭에서 진행을 볼 수 있어요.', '게임에 반영', 8);
  } else {
    ui.alert('요청 실패 (' + res.getResponseCode() + ')\n' + res.getContentText().slice(0, 300) +
      '\n\n토큰이 만료됐거나 권한(Actions: Read and write)이 없을 수 있어요.');
  }
}

/** 메뉴: deploy_status 탭으로 이동. */
function showDeployStatus() {
  const sheet = SpreadsheetApp.getActive().getSheetByName('deploy_status');
  if (!sheet) { SpreadsheetApp.getUi().alert('아직 반영 기록이 없어요.'); return; }
  SpreadsheetApp.getActive().setActiveSheet(sheet);
}

/** deploy_status 탭 기록 줄 바로 위(헤더 아래)에 한 줄 끼워 넣기 — 형식은 Tools/report_deploy_status.py 와 같다. */
function writeDeployStatus_(result, detail, link) {
  const ss = SpreadsheetApp.getActive();
  var sheet = ss.getSheetByName('deploy_status');
  if (!sheet) {
    sheet = ss.insertSheet('deploy_status');
    sheet.getRange(1, 1, 1, 4).setValues([['time', 'result', 'detail', 'link']]);
  }
  // 헤더 줄 찾기 (1행 ※설명이 있을 수 있음)
  const firstCol = sheet.getRange(1, 1, Math.min(3, sheet.getLastRow() || 1), 1).getValues();
  var headerRow = 1;
  for (var i = 0; i < firstCol.length; i++) { if (String(firstCol[i][0]) === 'time') { headerRow = i + 1; break; } }
  sheet.insertRowAfter(headerRow);
  const now = Utilities.formatDate(new Date(), 'Asia/Seoul', 'MM-dd HH:mm');
  sheet.getRange(headerRow + 1, 1, 1, 4).setValues([[now, result, detail, link]]);
}

function showWriteHelp() {
  SpreadsheetApp.getUi().alert(
    '브라우저에서 write_url 을 열었을 때\n' +
      '{"ok":true,"service":"yut-bubbles-write"} 가 보여야 합니다.\n' +
      'HTML/404가 나오면 웹 앱을 새 버전으로 다시 배포하세요.'
  );
}

/** 브라우저 확인용 — 이게 JSON이면 배포 성공 */
function doGet() {
  return jsonOut_({ ok: true, service: 'yut-bubbles-write' });
}

/**
 * POST body JSON:
 * {
 *   "token": "...",
 *   "tab": "yut_bubbles",
 *   "headers": ["id","note","text_ko",...],
 *   "rows": [["rabbit.yut","...", "..."], ...]
 * }
 */
function doPost(e) {
  try {
    if (!e || !e.postData || !e.postData.contents) {
      return jsonOut_({ ok: false, error: 'empty body' });
    }
    const body = JSON.parse(e.postData.contents);
    const expected = PropertiesService.getScriptProperties().getProperty('WRITE_TOKEN');
    if (expected && body.token !== expected) {
      return jsonOut_({ ok: false, error: 'unauthorized' });
    }

    const tab = (body.tab || 'yut_bubbles').toString();
    const headers = body.headers;
    const rows = body.rows;
    if (!Array.isArray(headers) || headers.length === 0) {
      return jsonOut_({ ok: false, error: 'headers required' });
    }
    if (!Array.isArray(rows)) {
      return jsonOut_({ ok: false, error: 'rows required' });
    }

    const spreadsheetId = (body.spreadsheetId || SPREADSHEET_ID).toString();
    const ss = SpreadsheetApp.openById(spreadsheetId);
    let sheet = ss.getSheetByName(tab);
    if (!sheet) {
      sheet = ss.insertSheet(tab);
    }

    sheet.clearContents();
    const values = [headers].concat(rows.map(function (r) {
      const out = [];
      for (var i = 0; i < headers.length; i++) {
        out.push(r[i] == null ? '' : String(r[i]));
      }
      return out;
    }));
    sheet.getRange(1, 1, values.length, headers.length).setValues(values);

    return jsonOut_({
      ok: true,
      spreadsheetId: ss.getId(),
      spreadsheetName: ss.getName(),
      tab: sheet.getName(),
      sheetId: sheet.getSheetId(),
      rows: rows.length,
      columns: headers.length,
      sheetNames: ss.getSheets().map(function (s) { return s.getName(); }),
    });
  } catch (err) {
    return jsonOut_({ ok: false, error: String(err) });
  }
}

function jsonOut_(obj) {
  return ContentService
    .createTextOutput(JSON.stringify(obj))
    .setMimeType(ContentService.MimeType.JSON);
}
