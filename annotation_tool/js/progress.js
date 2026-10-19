// How far through the library a listening run has got.
//
// Deliberately pure: the numbers shown to the listener have to agree with the
// numbers in the export, and they have to agree with themselves between visits.

/**
 * @param {Array} songs    the current library
 * @param {Array} sessions every session ever recorded
 */
export function summarizeLibrary(songs, sessions) {
  const list = songs || [];
  const finished = (sessions || []).filter((s) => s && s.endedAt);

  const bySong = new Map();
  for (const session of finished) {
    if (!session.songId) continue;
    const bucket = bySong.get(session.songId);
    if (bucket) bucket.push(session);
    else bySong.set(session.songId, [session]);
  }

  let annotated = 0;
  let coveredSeconds = 0;

  for (const song of list) {
    const songSessions = bySong.get(song.id);
    if (!songSessions?.length) continue;
    annotated += 1;

    // A bulk-imported song has no duration until it has been played once, so
    // fall back to what the session itself measured.
    const fromSong = Number(song.duration) || 0;
    let fromSessions = 0;
    for (const session of songSessions) {
      fromSessions = Math.max(
        fromSessions,
        Number(session.songDuration) || 0,
        Number(session.endedAtAudioTime) || 0,
      );
    }
    coveredSeconds += Math.max(fromSong, fromSessions);
  }

  const total = list.length;
  return {
    total,
    annotated,
    remaining: Math.max(0, total - annotated),
    coveredSeconds,
    // Repeat listens count here but never as extra coverage.
    sessions: finished.length,
  };
}

/** The next song to listen to: library order, first one without a finished session. */
export function pickNextUnannotated(songs, sessions) {
  const done = new Set((sessions || []).filter((s) => s?.endedAt).map((s) => s.songId));
  return [...(songs || [])].reverse().find((song) => !done.has(song.id)) || null;
}
