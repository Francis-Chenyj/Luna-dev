// Read-only browser check. Evaluate on a loaded Luna query after each viewport/language/theme change.
// This complements automated unit/API tests; it requires a real browser layout engine.
function inspectLunaLayout() {
  const problems = [];
  if(document.documentElement.scrollWidth>innerWidth+2)problems.push('Page overflows horizontally');
  const filters=document.getElementById('filters'),availability=document.getElementById('profile-availability');
  if(filters&&!filters.hidden){
    for(const control of filters.querySelectorAll('input,select'))if(control.getBoundingClientRect().width<120)problems.push(control.id+': filter control collapsed');
    if(availability&&!availability.hidden&&availability.getBoundingClientRect().top<filters.getBoundingClientRect().bottom-1)problems.push('Coverage hint overlaps filter controls');
  }
  const cards = [...document.querySelectorAll('.matchup-card')].filter(card => card.getBoundingClientRect().height > 0);
  if (!cards.length) problems.push('No visible matchup cards to check');
  for (const card of cards) {
    const name = card.querySelector('.matchup-text > strong')?.textContent || 'Unknown champion';
    const outer = card.getBoundingClientRect();
    const rows = ['.matchup-text > strong','.matchup-text > small','.matchup-text > .metric']
      .map(selector => card.querySelector(selector)).filter(element => element && element.textContent.trim());
    let previous = null;
    for (const row of rows) {
      const bounds = row.getBoundingClientRect();
      if (previous && bounds.top < previous.bottom - 0.5) problems.push(`${name}: text rows overlap`);
      if (bounds.left < outer.left - 0.5 || bounds.right > outer.right + 0.5 || bounds.bottom > outer.bottom + 0.5) {
        problems.push(`${name}: text extends outside its card`);
      }
      previous = bounds;
    }
  }
  return {passed:problems.length === 0,width:innerWidth,language:document.documentElement.lang,
    theme:document.documentElement.dataset.theme,cards:cards.length,problems};
}
export default inspectLunaLayout;
