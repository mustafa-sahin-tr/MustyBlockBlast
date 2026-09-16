#!/usr/bin/env bash
# ship-branch.sh — commit + push + PR + merge + update local main, tek komutla.
#
# Kullanım:
#   scripts/ship-branch.sh <branch-name> [commit-message]
#
# Örnek:
#   scripts/ship-branch.sh issue-127-vortex-magnet-tile "feat: spawn Vortex tile (#127)"
#
# commit-message verilmezse, git commit mesajı için varsayılan editörü açar.
# PR başlığı/gövdesi otomatik olarak branch'teki commit'lerden (gh pr create --fill) türetilir.
#
# Adımlar:
#   1. Verilen branch'e geç (yoksa hata verir — branch'i önce sen oluşturmalısın)
#   2. Değişiklikleri gözden geçirip onay alır, sonra stage eder
#   3. Commit eder
#   4. origin'e push eder (-u ile upstream ayarlanır)
#   5. Pull request açar (gh pr create --fill)
#   6. Onay alıp PR'ı merge eder ve remote branch'i siler
#   7. Local main'e geçip origin/main'i çeker

set -euo pipefail

BRANCH="${1:-}"
COMMIT_MESSAGE="${2:-}"

if [[ -z "$BRANCH" ]]; then
  echo "Kullanım: $0 <branch-name> [commit-message]" >&2
  exit 1
fi

REPO_ROOT="$(git rev-parse --show-toplevel)"
cd "$REPO_ROOT"

confirm() {
  local prompt="$1"
  read -r -p "$prompt [y/N] " reply
  [[ "$reply" =~ ^[Yy]$ ]]
}

CURRENT_BRANCH="$(git branch --show-current)"
if [[ "$CURRENT_BRANCH" != "$BRANCH" ]]; then
  if ! git show-ref --verify --quiet "refs/heads/$BRANCH"; then
    echo "Hata: '$BRANCH' local'de yok. Önce oluştur: git checkout -b $BRANCH" >&2
    exit 1
  fi
  echo "-> '$BRANCH' branch'ine geçiliyor"
  git checkout "$BRANCH"
fi

# Uncommitted değişiklik yoksa commit adımını atla
if [[ -z "$(git status --porcelain)" ]]; then
  echo "-> Stage edilecek değişiklik yok, commit adımı atlanıyor."
else
  echo "-> Değişiklikler:"
  git status --short
  echo
  if ! confirm "Bu değişiklikleri stage edip commit'leyeyim mi? (git add -A)"; then
    echo "İptal edildi." >&2
    exit 1
  fi

  git add -A
  echo
  echo "-> Stage edilenler (secret/credential görünen dosya var mı kontrol et):"
  git status --short

  if [[ -n "$COMMIT_MESSAGE" ]]; then
    git commit -m "$COMMIT_MESSAGE"
  else
    git commit
  fi
fi

echo
if ! confirm "'$BRANCH' origin'e push edilsin mi?"; then
  echo "Push atlandı, script burada duruyor." >&2
  exit 0
fi
git push -u origin "$BRANCH"

echo
if ! confirm "Pull request açılsın mı? (gh pr create --fill)"; then
  echo "PR açma atlandı, script burada duruyor." >&2
  exit 0
fi
PR_URL="$(gh pr create --fill)"
echo "$PR_URL"
PR_NUMBER="$(echo "$PR_URL" | grep -oE '[0-9]+$')"

echo
if ! confirm "PR #$PR_NUMBER merge edilip remote branch silinsin mi?"; then
  echo "Merge atlandı, script burada duruyor." >&2
  exit 0
fi
gh pr merge "$PR_NUMBER" --merge --delete-branch

echo
echo "-> main'e geçilip origin/main çekiliyor"
git checkout main
git pull origin main

echo
echo "Tamamlandı: PR #$PR_NUMBER merge edildi, local main güncel."
