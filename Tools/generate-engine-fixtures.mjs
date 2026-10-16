import { writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { gzipSync } from 'node:zlib';
import { createGame, press, advance, CONFIG } from '../../flip-race/src/engine.js';

// The original engine is read/imported only. Fixtures exercise ready, countdown,
// flips, ignored inputs, both tracks, many penalties, finish, and rematches.
const output = process.argv[2] ?? fileURLToPath(new URL('./engine-parity-fixtures.tsv.gz', import.meta.url));
const lines = ['# Generated from unchanged flip-race/src/engine.js; numeric comparison tolerance < 1e-8.'];
const val = value => value === null || value === undefined ? 'null' : String(value);
let game;
function step(operation, argument) {
  let accepted = null;
  if (operation === 'new') game = createGame(argument);
  else if (operation === 'press') accepted = press(game, argument);
  else advance(game, argument);
  const state = [accepted, game.phase, game.elapsed, game.countdownRemaining, game.winner, game.winnerAt];
  for (const p of game.players) state.push(p.id, p.ready, p.distance, p.y, p.flipping,
    p.flipFrom, p.flipTo, p.flipElapsed, p.recovering, p.collisions, p.finishTime,
    [...p.hitObstacleIds].sort().join(','), p.lastHit?.id, p.lastHit?.message, p.lastHit?.at);
  lines.push([operation, argument, ...state].map(val).join('\t'));
}
let seed = 0x4f1a9137;
const random = () => ((seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0) / 0x100000000);
for (const mode of ['race', 'practice']) {
  for (let run = 0; run < 48; run++) {
    step('new', mode);
    step('press', 0);
    step('advance', random());
    step('press', 1);
    step('press', 0);
    step('advance', 2.99);
    step('advance', .01 + random() / 10);
    for (let frame = 0; frame < 240; frame++) {
      const action = random();
      if (action < .33) step('press', 0);
      else if (action < .66) step('press', 1);
      else if (action < .69) step('press', -1);
      step('advance', [1 / 30, 1 / 60, 1 / 144, .317, .8 - 5e-10, 2.5][Math.floor(random() * 6)]);
    }
    step('advance', 100);
    step('press', 1);
    step('press', 0);
    step('advance', 3.25);
  }
  const route = mode === 'race' ? [400, 980, 2040, 2500, 3318, 4950, 5410, 6118, 7110] : [450, 1100, 2050, 2718];
  for (const offset of [-.08, 0, .08]) {
    step('new', mode); step('press', 0); step('press', 1); step('advance', 3);
    let elapsed = 0;
    for (const distance of route) {
      const time = distance / CONFIG.speed + offset;
      step('advance', time - elapsed);
      elapsed = time;
      step('press', 0); step('press', 1);
    }
    step('advance', 100);
  }
}
const data = lines.join('\n') + '\n';
await writeFile(output, output.endsWith('.gz') ? gzipSync(data, { level: 9 }) : data);
console.log(`Wrote ${lines.length - 1} JS baseline snapshots to ${output}`);
