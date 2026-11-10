"""Train one fresh Simple run, then evaluate its exported model in the Unity Editor.

Launch using the course's ML-Agents Python environment. No scene is saved and
no existing run is overwritten. See records/Simple/experiment_plan.txt.
"""
import argparse
import csv
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time
from datetime import datetime, timezone

p = argparse.ArgumentParser()
p.add_argument('--unity', required=True)
p.add_argument('--config', default='config/simple_e1_batch512.yaml')
p.add_argument('--run-id', default='Team1_Simple_E1_batch512_seed1')
args = p.parse_args()
root = Path(__file__).resolve().parents[1]
record = root / 'records' / 'Simple' / args.run_id
result = root / 'results' / args.run_id
if result.exists():
    raise SystemExit('Run already exists; choose a new run ID. This script never overwrites results.')
record.mkdir(parents=True, exist_ok=True)
stop = record / 'stop_unity.signal'
if stop.exists():
    raise SystemExit('Stop marker already exists; inspect previous attempt before restarting.')
env = dict(os.environ, PYTHONIOENCODING='utf-8', PYTHONUNBUFFERED='1')
flags = subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0
trainer = None
unity = None
state = {'run_id': args.run_id, 'config': args.config, 'seed': 1, 'budget_steps': 500000,
         'start_utc': datetime.now(timezone.utc).isoformat(), 'status': 'preparing'}
state['code_commit'] = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=root, text=True).strip()

def save_status(status, **kw):
    state.update(status=status, updated_utc=datetime.now(timezone.utc).isoformat(), **kw)
    temporary = record / 'status.tmp'
    temporary.write_text(json.dumps(state, indent=2, ensure_ascii=False), encoding='utf-8')
    temporary.replace(record / 'status.json')
    print(status, kw, flush=True)

def readlog(path):
    return path.read_text(encoding='utf-8', errors='replace') if path.exists() else ''

def unity_args(mode, log):
    return [args.unity, '-batchmode', '-nographics', '-projectPath', str(root),
            '-executeMethod', 'SimpleExperimentRunner.Run', '-experiment-mode', mode,
            '-logFile', str(log)]

def stop_editor():
    if unity is not None and unity.poll() is None:
        stop.write_text('Trainer ended; close only this automated Unity process.', encoding='utf-8')
        try:
            unity.wait(timeout=45)
        except subprocess.TimeoutExpired:
            unity.terminate()
            unity.wait(timeout=30)

def summarize(path):
    with path.open(encoding='utf-8-sig', newline='') as f:
        rows = list(csv.DictReader(f))
    if len(rows) != 10:
        raise RuntimeError('Evaluation must have exactly ten completed rounds')
    return {'rounds': len(rows), 'wins': sum(r['outcome']=='W' for r in rows),
            'draws': sum(r['outcome']=='D' for r in rows), 'losses': sum(r['outcome']=='L' for r in rows),
            'mean_duration_seconds': sum(float(r['duration_seconds']) for r in rows)/len(rows),
            'mean_reward': sum(float(r['player_reward']) for r in rows)/len(rows)}

try:
    import yaml
    baseline = yaml.safe_load((root/'config/assignment2_ppo.yaml').read_text(encoding='utf-8-sig'))
    experiment = yaml.safe_load((root/args.config).read_text(encoding='utf-8-sig'))
    expected = json.loads(json.dumps(baseline))
    expected['behaviors']['RollingControl']['hyperparameters']['batch_size'] = 512
    if experiment != expected:
        raise RuntimeError('This experiment must change only batch_size to 512')
    state['config_sha256'] = hashlib.sha256((root/args.config).read_bytes()).hexdigest()
    trainlog = record/'trainer.log'
    cmd = [sys.executable, '-u', '-m', 'mlagents.trainers.learn', args.config,
           '--run-id='+args.run_id, '--seed=1', '--timeout-wait=300']
    state['training_command'] = cmd
    with trainlog.open('w', encoding='utf-8') as tf:
        trainer = subprocess.Popen(cmd, cwd=root, env=env, stdout=tf, stderr=subprocess.STDOUT, creationflags=flags)
        save_status('waiting_for_trainer', trainer_pid=trainer.pid)
        deadline = time.monotonic()+120
        while 'Listening on port' not in readlog(trainlog):
            if trainer.poll() is not None:
                raise RuntimeError('Trainer exited before listening; inspect trainer.log')
            if time.monotonic()>deadline:
                raise RuntimeError('Trainer startup timeout')
            time.sleep(2)
        cmd = unity_args('train', record/'unity_training.log')+['-experiment-stop',str(stop)]
        unity = subprocess.Popen(cmd, cwd=root, env=env, creationflags=flags)
        save_status('connecting_unity', unity_pid=unity.pid)
        deadline = time.monotonic()+10800
        last_step = 0
        while trainer.poll() is None:
            if unity.poll() is not None:
                raise RuntimeError('Unity exited during training; inspect unity_training.log')
            matches = re.findall(r'Step:\s*(\d+)', readlog(trainlog))
            step = int(matches[-1]) if matches else 0
            if step>last_step:
                save_status('training', step=step)
                last_step=step
            if time.monotonic()>deadline:
                raise RuntimeError('Training exceeded 3-hour timeout')
            time.sleep(5)
        if trainer.returncode != 0:
            raise RuntimeError('Trainer failed, exit code '+str(trainer.returncode))
    stop_editor()
    training_status = json.loads((result/'run_logs/training_status.json').read_text())
    final = training_status['RollingControl']['final_checkpoint']
    if final['steps']<500000 or not (result/'RollingControl.onnx').exists():
        raise RuntimeError('Trainer ended without completing the full budget and export')
    save_status('training_complete', step=final['steps'], training_reward=final['reward'])
    dest = root/'Assets'/'ExperimentModels'
    dest.mkdir(exist_ok=True)
    model_name = args.run_id+'.onnx'
    shutil.copy2(result/'RollingControl.onnx', dest/model_name)
    cmd = unity_args('eval',record/'unity_evaluation.log')+[
        '-experiment-model','Assets/ExperimentModels/'+model_name,
        '-experiment-rounds','10','-experiment-seed','1',
        '-experiment-output',str(record/'evaluation_rounds.csv')]
    unity = subprocess.Popen(cmd, cwd=root, env=env, creationflags=flags)
    save_status('evaluating', unity_pid=unity.pid)
    unity.wait(timeout=900)
    if unity.returncode != 0:
        raise RuntimeError('Evaluation failed, exit code '+str(unity.returncode))
    comparison = {'B0':summarize(root/'records/Simple/B0_evaluation_20261110/rounds.csv'),
                  'E1':summarize(record/'evaluation_rounds.csv')}
    from tensorboard.backend.event_processing.event_accumulator import EventAccumulator
    curves={}
    for label,run in [('B0','Team1_Simple_B0_seed1'),('E1',args.run_id)]:
        accumulator=EventAccumulator(str(root/'results'/run/'RollingControl'))
        accumulator.Reload()
        curves[label]={tag:[{'step':v.step,'value':v.value} for v in accumulator.Scalars(tag)]
                       for tag in ['Environment/Cumulative Reward','Environment/Episode Length','Policy/Entropy']}
    (record/'training_curves.json').write_text(json.dumps(curves,indent=2),encoding='utf-8')
    for label in ['B0','E1']:
        reward=curves[label]['Environment/Cumulative Reward']
        comparison[label]['reward_at_steps']={str(n):next((v['value'] for v in reward if v['step']==n),None)
                                                for n in [100000,200000,300000,500000]}
    (record/'comparison.json').write_text(json.dumps(comparison,indent=2),encoding='utf-8')
    lines=['Simple E1 automatic comparison','Only batch_size changed: 1024 -> 512.',
           'Evaluation: Simple, ConservativeCenter, fixed spawns, seed 1, CPU inference, 10 rounds.',
           'Only 10 evaluation rounds and one training seed; do not infer a reliable general improvement.']
    for label,data in comparison.items():
        lines.append(label+': '+json.dumps(data,ensure_ascii=False))
    lines.append('Review curves and gameplay before selecting Simple_Final. E1 is a candidate, not an automatic replacement for B0.')
    (record/'comparison.txt').write_text('\n'.join(lines),encoding='utf-8')
    save_status('complete', comparison=comparison)
except Exception as exc:
    save_status('failed', error=str(exc))
    if trainer is not None and trainer.poll() is None:
        trainer.terminate()
        trainer.wait(timeout=30)
    stop_editor()
    raise
