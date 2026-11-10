"""Re-evaluate B0 with the same corrected runner used for E1 after training ends."""
import argparse
import csv
import json
from pathlib import Path
import subprocess
import time

p=argparse.ArgumentParser()
p.add_argument('--unity',required=True)
p.add_argument('--run-id',required=True)
args=p.parse_args()
root=Path(__file__).resolve().parents[1]
folder=root/'records/Simple'/args.run_id
audit=folder/'comparison_verification.json'
def status(value,**kw):
    audit.write_text(json.dumps({'status':value,**kw},indent=2),encoding='utf-8')
status('waiting_for_training_and_E1_evaluation')
try:
    deadline=time.monotonic()+12000
    while True:
        state=json.loads((folder/'status.json').read_text(encoding='utf-8'))
        if state['status']=='failed':
            raise RuntimeError('Training supervisor failed; baseline verification not started')
        if state['status']=='complete':
            break
        if time.monotonic()>deadline:
            raise RuntimeError('Timed out waiting for training')
        time.sleep(10)
    status('evaluating_B0_with_matching_CPU_protocol')
    rounds=folder/'B0_verified_rounds.csv'
    cmd=[args.unity,'-batchmode','-nographics','-projectPath',str(root),
         '-executeMethod','SimpleExperimentRunner.Run','-experiment-mode','eval',
         '-experiment-model','Assets/RollingControl.onnx','-experiment-seed','1',
         '-experiment-rounds','10','-experiment-output',str(rounds),
         '-logFile',str(folder/'unity_B0_verification.log')]
    result=subprocess.run(cmd,cwd=root,creationflags=subprocess.CREATE_NO_WINDOW,timeout=900)
    if result.returncode!=0:
        raise RuntimeError('B0 verification editor failed: '+str(result.returncode))
    with rounds.open(encoding='utf-8-sig',newline='') as f:
        rows=list(csv.DictReader(f))
    if len(rows)!=10:
        raise RuntimeError('Expected exactly ten B0 rounds')
    comparison=json.loads((folder/'comparison.json').read_text(encoding='utf-8'))
    comparison['B0'].update(rounds=10,wins=sum(r['outcome']=='W' for r in rows),
        draws=sum(r['outcome']=='D' for r in rows),losses=sum(r['outcome']=='L' for r in rows),
        mean_duration_seconds=sum(float(r['duration_seconds']) for r in rows)/10,
        mean_reward=sum(float(r['player_reward']) for r in rows)/10)
    comparison['protocol']='Same corrected Unity runner; CPU inference, seed 1, 10 rounds, Simple original opponent and spawns.'
    (folder/'verified_comparison.json').write_text(json.dumps(comparison,indent=2),encoding='utf-8')
    (folder/'verified_comparison.txt').write_text(
        'Use this verified comparison instead of the earlier pilot comparison.\n'+
        json.dumps(comparison,indent=2)+'\nOne seed and ten rounds are limited evidence; review before selecting Final.',encoding='utf-8')
    status('complete',comparison_file='verified_comparison.json')
except Exception as exc:
    status('failed',error=str(exc))
    raise
