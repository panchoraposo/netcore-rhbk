# Ansible bootstrap (OpenShift)

This folder contains an optional bootstrap playbook to:

- Install or validate required Operators (OpenShift GitOps, RHBK Operator)
- Create Argo CD `Application` resources for the demo

## Prerequisites
- `oc` logged in as a cluster admin (or with permissions to install Operators and create resources in `openshift-gitops` / `openshift-operators`)
- Ansible installed
- Kubernetes collection installed: `kubernetes.core`

## Install collections

```bash
ansible-galaxy collection install -r requirements.yml
```

## Run bootstrap

```bash
ansible-playbook -e repo_url="https://your.git/repo.git" bootstrap.yml
```

## Notes
- Operator installation can be environment-specific. For regulated/banking environments, prefer **manual approval** for Operator upgrades.
